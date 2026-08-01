using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class DocumentEditPlane
{
    static async Task<string> EditAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        // Resolve path key first (may open); then hold per-path gate for apply+flush only.
        // Diagnostics run AFTER the gate: Roslyn/MSBuild is not parallel-safe and held the
        // mutate lock for minutes (dogfood hang on parallel anchor edits).
        var pathKey = ResolvePathKey(store, session, args);
        var pathExistedBefore = File.Exists(pathKey);
        var applied = await store.MutateAsync(pathKey, async () =>
        {
            // Must not call Resolve/Open here: they take PathMutateGate again → self-deadlock
            // when the buffer was never opened (first edit_op=set_text on a cold path).
            // pathKey is already session-resolved (ProjectRoot); do not re-GetFullPath relative path=.
            var buf = store.ResolveUnlocked(pathKey, OptString(args, "doc_id"));
            var snapshotText = buf.Text;
            var snapshotVersion = buf.Version;
            var snapshotDirty = buf.Dirty;
            var op = RequireString(args, "op").Trim().ToLowerInvariant();
            object? anchorResolved = null;
            try
            {
                switch (op)
                {
                    case "set_text":
                        store.ApplySetText(buf, OptString(args, "text") ?? "");
                        break;
                    case "replace":
                        store.ApplyReplace(buf, RequireString(args, "old_string"), OptString(args, "new_string") ?? "");
                        break;
                    case "replace_range":
                        store.ApplyReplaceRange(
                            buf,
                            RequireInt(args, "start_line"),
                            RequireInt(args, "start_column"),
                            RequireInt(args, "end_line"),
                            RequireInt(args, "end_column"),
                            OptString(args, "text") ?? "");
                        break;
                    case "anchor":
                        anchorResolved = ApplyAnchorEdit(store, session, buf, args);
                        break;
                    default:
                        throw new ArgumentException(
                            "op must be set_text | replace | replace_range | anchor.");
                }

                var flush = BoolOr(args, "flush", defaultValue: true);
                var allowShrink = op is "replace" or "replace_range" or "anchor"
                    || BoolOr(args, "allow_shrink", defaultValue: false);
                if (flush)
                {
                    store.FlushUnlocked(buf, allowShrink);
                    if (string.Equals(buf.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                    {
                        RoslynMcp.ServiceLayer.DiagnosticsResultCache.InvalidatePath(buf.Path);
                        _ = RoslynMcp.ServiceLayer.MsBuildWorkspaceHost.TryApplyDocumentText(buf.Path, buf.Text);
                    }

                    // Version bumped on apply — clear stale diag pointer until recompute.
                    buf.LastDiagnosedVersion = null;
                }

                return new EditApplied(buf, op, flush, allowShrink, anchorResolved, snapshotText);
            }
            catch
            {
                buf.Text = snapshotText;
                buf.Version = snapshotVersion;
                buf.Dirty = snapshotDirty;
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        EditorComfort.RecordEdit(applied.Buf.Path, applied.BeforeText, applied.Buf.Text, applied.Op);
        EditorComfort.RememberFile(applied.Buf.Path);
        AdxMutateTrace.Record(
            applied.Buf.Path,
            applied.Op,
            isCreate: false,
            pathExistedBefore: pathExistedBefore);
        if (applied.Anchor is not null)
        {
            // Best-effort locus from anchor edit result if it carries a wire.
        }

        var diagnose = BoolOr(args, "diagnose", defaultValue: true);
        object? diagnostics = null;
        string? diagNote = null;
        if (diagnose)
            (diagnostics, diagNote) = await TryDiagnoseBudgetedAsync(
                applied.Buf, store, session, byDomain, cancellationToken).ConfigureAwait(false);

        var thrash = ThrashHint(applied);

        return JsonSerializer.Serialize(new
        {
            schema = "doc_edit/v0",
            ok = true,
            op = applied.Op,
            flushed = applied.Flushed,
            allow_shrink = applied.AllowShrink,
            anchor = applied.Anchor,
            meta = applied.Buf.ToMeta(),
            diagnostics,
            diagnostics_note = diagNote,
            quality = QualityGates.ForEditResult(applied.Buf, session.ProjectRoot),
            comfort = EditorComfort.Snap(),
            thrash,
            hint = thrash?.hint,
            mutate = "path_serialized"
        }, Pretty);
    }

    /// <summary>
    /// Thick set_text on large buffers is legal but stressful — nudge sniper/anchor for next cuts.
    /// </summary>
    static ThrashCard? ThrashHint(EditApplied applied)
    {
        if (!string.Equals(applied.Op, "set_text", StringComparison.OrdinalIgnoreCase))
            return null;

        var beforeLines = CountNewlines(applied.BeforeText);
        var afterLines = CountNewlines(applied.Buf.Text);
        const int warnLines = 350;
        if (beforeLines < warnLines && afterLines < warnLines && applied.Buf.Text.Length < 48_000)
            return null;

        return new ThrashCard(
            "set_text_large",
            beforeLines,
            afterLines,
            applied.Buf.Text.Length,
            "Large set_text — next edits: edit_op=anchor|replace or go=scope sniper (not another whole-file set_text).");
    }

    static int CountNewlines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var n = 1;
        foreach (var ch in text)
        {
            if (ch == '\n') n++;
        }

        return n;
    }

    sealed record ThrashCard(string kind, int before_lines, int after_lines, int chars, string hint);

    sealed record EditApplied(
        DocBuffer Buf,
        string Op,
        bool Flushed,
        bool AllowShrink,
        object? Anchor,
        string BeforeText);

    /// <summary>
    /// Single-flight + soft timeout so parallel edits never wedge on Roslyn workspace load.
    /// </summary>
    static async Task<(object? diagnostics, string? note)> TryDiagnoseBudgetedAsync(
        DocBuffer buf,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(DiagnosticsBudget);
        var entered = false;
        try
        {
            await DiagnosticsGate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            var work = TryDiagnoseAsync(buf, store, session, byDomain, flush: false, linked.Token);
            var finished = await Task.WhenAny(work, Task.Delay(DiagnosticsBudget, linked.Token))
                .ConfigureAwait(false);
            if (finished != work)
            {
                return (null,
                    $"diagnostics deferred (budget {DiagnosticsBudget.TotalSeconds:0}s; " +
                    "edit already flushed — call cdp_buffer op=diagnostics).");
            }

            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null,
                $"diagnostics deferred (budget {DiagnosticsBudget.TotalSeconds:0}s exceeded after flush; " +
                "call cdp_buffer op=diagnostics).");
        }
        catch (OperationCanceledException)
        {
            return (null, "diagnostics cancelled.");
        }
        finally
        {
            if (entered)
                DiagnosticsGate.Release();
        }
    }

}
