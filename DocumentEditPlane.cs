using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// CDP document plane: open → structured edit → flush → diagnostics in the same tool result.
/// Almost-online while the agent keeps the turn.
/// </summary>
internal static class DocumentEditPlane
{
    static readonly SemaphoreSlim DiagnosticsGate = new(1, 1);
    static readonly TimeSpan DiagnosticsBudget = TimeSpan.FromSeconds(12);

    public static bool IsDocTool(string name) =>
        name is "cdp_buffer" or "cdp_doc" or "cdp_doc_scene" or "cdp_doc_open" or "cdp_doc_read" or "cdp_doc_edit"
            or "cdp_doc_diagnostics" or "cdp_doc_close";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<string> DispatchAsync(
        string name,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        // Omnibus: cdp_buffer(op=…) — preferred. Legacy cdp_doc / cdp_doc_* still routed.
        var op = name switch
        {
            "cdp_buffer" or "cdp_doc" => RequireString(args, "op").Trim().ToLowerInvariant(),
            "cdp_doc_scene" => "scene",
            "cdp_doc_open" => "open",
            "cdp_doc_read" => "read",
            "cdp_doc_edit" => "edit",
            "cdp_doc_diagnostics" => "diagnostics",
            "cdp_doc_close" => "close",
            _ => throw new ArgumentException($"Unknown document tool: {name}")
        };

        var editArgs = args;
        if ((name is "cdp_buffer" or "cdp_doc") && op == "edit")
        {
            var dict = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
            if (!dict.TryGetValue("edit_op", out var eo) || eo.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(eo.GetString()))
            {
                throw new ArgumentException(
                    "cdp_buffer op=edit requires edit_op=set_text|replace|replace_range|anchor.");
            }

            dict["op"] = eo;
            editArgs = dict;
        }

        return op switch
        {
            "scene" => JsonSerializer.Serialize(store.Scene(), Pretty),
            "open" => await OpenAsync(store, session, byDomain, args, cancellationToken).ConfigureAwait(false),
            "create" => await CreateAsync(store, session, byDomain, args, cancellationToken).ConfigureAwait(false),
            "read" => Read(store, session, args),
            "edit" => await EditAsync(store, session, byDomain, editArgs, cancellationToken).ConfigureAwait(false),
            "diagnostics" => await DiagnosticsAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            "take" => await TakeShip.TakeAsync(
                    store, session, byDomain, args, TryDiagnoseScopedAsync, cancellationToken)
                .ConfigureAwait(false),
            "share" => IdeShare.DispatchShare(store, session, args),
            "close" => Close(store, session, args),
            "reload" => Reload(store, session, args),
            "keep_disk" => KeepDisk(store, session, args),
            "disk_peek" => DiskPeek(store, session, args),
            _ when EditorComfort.IsComfortOp(op) => EditorComfort.Dispatch(store, session, op, args),
            _ => throw new ArgumentException(
                "cdp_buffer op must be scene|open|create|read|edit|diagnostics|take|share|close|reload|keep_disk|disk_peek|" +
                "undo|redo|history|copy|cut|paste|put|clipboard|find|find_all|replace_all|back|forward|nav|recent_files|scratch.")
        };
    }

    static string Reload(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            var hit = store.ReloadAllDrifted();
            return JsonSerializer.Serialize(new
            {
                schema = "doc_reload/v0",
                ok = true,
                op = "reload",
                count = hit.Count,
                reloaded = hit.Select(b => b.ToMeta()).ToArray(),
                hint = "Reloaded all buffers with disk drift (VS Reload). Pass path= for one file."
            }, Pretty);
        }

        var full = ResolveUserPath(session, path);
        var buf = store.ReloadFromDisk(full);
        EditorComfort.ClearStack(full);
        EditorComfort.RememberFile(full);
        return JsonSerializer.Serialize(new
        {
            schema = "doc_reload/v0",
            ok = true,
            op = "reload",
            count = 1,
            meta = buf.ToMeta(),
            hint = "Buffer ← disk. Dirty cleared. Edit undo stack cleared for this file."
        }, Pretty);
    }

    static string KeepDisk(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            var hit = store.KeepAllDrifted();
            return JsonSerializer.Serialize(new
            {
                schema = "doc_keep_disk/v0",
                ok = true,
                op = "keep_disk",
                count = hit.Count,
                kept = hit.Select(b => b.ToMeta()).ToArray(),
                hint = "Kept memory for all drifted buffers (VS Don't Reload). Pass path= for one file."
            }, Pretty);
        }

        var full = ResolveUserPath(session, path);
        var buf = store.KeepDisk(full);
        return JsonSerializer.Serialize(new
        {
            schema = "doc_keep_disk/v0",
            ok = true,
            op = "keep_disk",
            count = 1,
            meta = buf.ToMeta(),
            hint = "Kept memory; silenced disk_changed (VS Don't Reload)."
        }, Pretty);
    }

    static string DiskPeek(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        var pad = IntOrNull(args, "pad") ?? 2;
        if (string.IsNullOrWhiteSpace(path))
        {
            var peeks = store.PeekAllDrifted(pad);
            return JsonSerializer.Serialize(new
            {
                schema = "doc_disk_peek_batch/v0",
                ok = true,
                op = "disk_peek",
                count = peeks.Count,
                peeks,
                hint = "Glance mem vs disk for all drifted. Pass path= for one file. Then go=reload|keep_disk."
            }, Pretty);
        }

        var full = ResolveUserPath(session, path);
        return JsonSerializer.Serialize(store.PeekDisk(full, pad), Pretty);
    }

    static async Task<string> OpenAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var path = ResolveUserPath(session, RequireString(args, "path"));
        // Default false: open of large csharp projects can dump hundreds of Roslyn items and hang the host.
        var diagnose = BoolOr(args, "diagnose", defaultValue: false);
        var refresh = BoolOr(args, "refresh", defaultValue: false);
        var buf = store.Open(path, refresh);
        EditorComfort.RememberFile(path);
        EditorComfort.PushLocus(session, path);
        DeskBookmark.Save(session, store);
        object? diagnostics = null;
        string? diagNote = null;
        if (diagnose)
            (diagnostics, diagNote) = await TryDiagnoseAsync(buf, store, session, byDomain, flush: false, cancellationToken)
                .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            schema = "doc_open/v0",
            ok = true,
            meta = buf.ToMeta(),
            comfort = EditorComfort.Snap(),
            diagnostics,
            diagnostics_note = diagNote,
            hint =
                "Prefer edit_op=anchor with Anchor wire [F:;M:;K:] (csharp). " +
                "Comfort: undo/redo/copy/paste/find/back. Fallback: set_text|replace|replace_range."
        }, Pretty);
    }

    static async Task<string> CreateAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var path = ResolveUserPath(session, RequireString(args, "path"));
        var overwrite = BoolOr(args, "overwrite", defaultValue: false);
        var text = OptString(args, "text");
        var diagnose = BoolOr(args, "diagnose", defaultValue: true);
        var buf = store.Create(path, text, overwrite);
        object? diagnostics = null;
        string? diagNote = null;
        if (diagnose)
            (diagnostics, diagNote) = await TryDiagnoseAsync(buf, store, session, byDomain, flush: false, cancellationToken)
                .ConfigureAwait(false);

        return JsonSerializer.Serialize(new
        {
            schema = "doc_create/v0",
            ok = true,
            meta = buf.ToMeta(),
            diagnostics,
            diagnostics_note = diagNote
        }, Pretty);
    }

    static string Read(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        var resolved = path is { Length: > 0 } ? ResolveUserPath(session, path) : null;
        var buf = store.Resolve(resolved, OptString(args, "doc_id"));
        EditorComfort.RememberFile(buf.Path);
        EditorComfort.PushLocus(session, buf.Path);
        int? start = IntOrNull(args, "start_line");
        int? end = IntOrNull(args, "end_line");
        return JsonSerializer.Serialize(buf.ToReadResult(start, end), Pretty);
    }

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

    static object ApplyAnchorEdit(
        DocumentBufferStore store,
        SessionContext session,
        DocBuffer buf,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var wire = OptString(args, "anchor") ?? OptString(args, "at")
            ?? throw new ArgumentException("edit_op=anchor requires anchor= (or at=) bracket wire [F:;M:;K:] or [F:;X:;A:].");
        var replacement = OptString(args, "text") ?? OptString(args, "new_string")
            ?? throw new ArgumentException("edit_op=anchor requires text= (body for place=; default place=replace overwrites locus).");
        var place = NormalizeAnchorPlace(OptString(args, "place") ?? OptString(args, "at_place"));

        BracketLocate.Span span;
        try
        {
            span = BracketLocate.Parse(wire);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Anchor wire invalid: {ex.Message}");
        }

        var filePath = ResolveAnchorFilePath(buf, session, span, OptString(args, "path"));
        if (!string.Equals(filePath, buf.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Anchor F: resolves to '{filePath}' but edit gate/buffer is '{buf.Path}'. " +
                "Pass path= matching F: (or omit path and put absolute/relative path in F:).");
        }

        var family = BracketLocate.ClassifyFamily(span, out var familyError);
        if (familyError is not null)
            throw new ArgumentException(familyError);
        if (family == BracketLocate.AxisFamily.None)
            throw new ArgumentException("Anchor needs csharp axes (M/L/S/K) or xml axes (X/A).");
        if (family == BracketLocate.AxisFamily.Navigation)
            throw new ArgumentException(
                "Family:navigation is land-only — use cdp_land (not edit_op=anchor).");

        if (family == BracketLocate.AxisFamily.Csharp)
        {
            if (!string.Equals(buf.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"axes_mismatch: csharp axes on language={buf.Language}. path={buf.Path}");

            if (!BracketSyntaxResolve.TryResolve(buf.Path, buf.Text, span, out var range, out var detail))
                throw new ArgumentException($"Anchor resolve failed ({detail}): {wire}");

            ApplyPlacedRange(
                store, buf, place,
                range.LineStart, range.ColumnStart, range.LineEnd, range.ColumnEnd,
                replacement);

            return new
            {
                family = "csharp",
                wire = BracketLocate.Format(span),
                resolve = detail,
                place,
                range = new
                {
                    start_line = range.LineStart,
                    start_column = range.ColumnStart,
                    end_line = range.LineEnd,
                    end_column = range.ColumnEnd
                }
            };
        }

        // Xml family
        if (!string.Equals(buf.Language, "xml", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"axes_mismatch: xml axes on language={buf.Language}. path={buf.Path}");

        if (!BracketXmlResolve.TryResolve(buf.Path, buf.Text, span, out var xml, out var xmlDetail))
            throw new ArgumentException($"Anchor resolve failed ({xmlDetail}): {wire}");

        var textToWrite = replacement;
        if (xml.Insert)
        {
            // +K:Element insert is its own place semantics — do not also honor place=before/after
            // as a second axis (would double-apply). place= must be omit/replace.
            if (place is not "replace")
                throw new ArgumentException(
                    "xml +K:Element insert already places the node; omit place= or use place=replace.");
            if (string.IsNullOrWhiteSpace(xml.InsertElementName))
                throw new ArgumentException("xml_insert_missing_element_name");
            textToWrite = BracketXmlResolve.BuildInsertElement(
                xml.InsertElementName,
                replacement,
                xml.InsertIndent ?? "  ");
        }

        ApplyPlacedRange(
            store, buf, place,
            xml.Range.LineStart, xml.Range.ColumnStart, xml.Range.LineEnd, xml.Range.ColumnEnd,
            textToWrite);

        return new
        {
            family = "xml",
            wire = BracketLocate.Format(span),
            resolve = xml.Detail,
            insert = xml.Insert,
            place,
            range = new
            {
                start_line = xml.Range.LineStart,
                start_column = xml.Range.ColumnStart,
                end_line = xml.Range.LineEnd,
                end_column = xml.Range.ColumnEnd
            }
        };
    }

    /// <summary>
    /// <c>place=</c> for <c>edit_op=anchor</c>: before|after insert at locus edges; replace (default) overwrites.
    /// Silent ignore was ultra-critical — agents passed place=before and wiped the member.
    /// </summary>
    static string NormalizeAnchorPlace(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "replace";
        var p = raw.Trim().ToLowerInvariant();
        return p switch
        {
            "before" or "pre" or "b" => "before",
            "after" or "post" or "a" => "after",
            "replace" or "over" or "r" or "into" => "replace",
            "sniper" or "hold" or "target" => throw new ArgumentException(
                "place=sniper is paste/put only — for anchor use place=before|after|replace."),
            _ => throw new ArgumentException(
                $"Unknown place='{raw}' for edit_op=anchor — use before|after|replace.")
        };
    }

    static void ApplyPlacedRange(
        DocumentBufferStore store,
        DocBuffer buf,
        string place,
        int lineStart,
        int colStart,
        int lineEnd,
        int colEnd,
        string text)
    {
        if (place == "replace")
        {
            store.ApplyReplaceRange(buf, lineStart, colStart, lineEnd, colEnd, text);
            return;
        }

        if (place == "before")
        {
            // Insert at line start (col 1), not member token col — otherwise leading
            // indent of the locus sticks to the inserted text and the old member loses it.
            store.ApplyReplaceRange(buf, lineStart, 1, lineStart, 1, text);
            return;
        }

        // after — exclusive end point of resolved locus
        store.ApplyReplaceRange(buf, lineEnd, colEnd, lineEnd, colEnd, text);
    }

    /// <summary>
    /// Relative <c>path=</c> → <see cref="SessionContext.ProjectRoot"/> (else process cwd).
    /// Absolute unchanged. Aligns buffer plane with anchor <c>F:</c> resolve — not MCP host home.
    /// </summary>
    static string ResolveUserPath(SessionContext session, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Trim();
        if (Path.IsPathRooted(p))
            return Path.GetFullPath(p);

        var root = session.ProjectRoot is { Length: > 0 } pr
            ? pr
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, p));
    }

    static string ResolveAnchorFilePath(
        DocBuffer buf,
        SessionContext session,
        BracketLocate.Span span,
        string? pathArg)
    {
        if (pathArg is { Length: > 0 })
            return ResolveUserPath(session, pathArg);

        if (!string.IsNullOrWhiteSpace(span.File))
        {
            var f = span.File.Trim();
            if (Path.IsPathRooted(f))
                return Path.GetFullPath(f);

            var root = session.ProjectRoot
                ?? Path.GetDirectoryName(buf.Path)
                ?? Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, f));
        }

        return buf.Path;
    }

    static string ResolvePathKey(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var path = OptString(args, "path");
        if (path is { Length: > 0 })
            return ResolveUserPath(session, path);

        var wire = OptString(args, "anchor") ?? OptString(args, "at");
        if (wire is { Length: > 0 })
        {
            var span = BracketLocate.Parse(wire);
            if (!string.IsNullOrWhiteSpace(span.File))
            {
                var f = span.File.Trim();
                if (Path.IsPathRooted(f))
                    return Path.GetFullPath(f);
                var root = session.ProjectRoot ?? Directory.GetCurrentDirectory();
                return Path.GetFullPath(Path.Combine(root, f));
            }
        }

        if (OptString(args, "doc_id") is { Length: > 0 })
            return store.Resolve(null, OptString(args, "doc_id")).Path;

        throw new ArgumentException(
            "edit needs path=, doc_id=, or anchor with F: file (project-relative or absolute).");
    }

    static async Task<string> DiagnosticsAsync(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var path = OptString(args, "path");
        var resolved = path is { Length: > 0 } ? ResolveUserPath(session, path) : null;
        var buf = store.Resolve(resolved, OptString(args, "doc_id"));
        var flush = BoolOr(args, "flush", defaultValue: true);
        // force=true → recompute even if version unchanged. refresh=false kept as soft "prefer cache".
        var force = BoolOr(args, "force", defaultValue: false);
        var refresh = BoolOr(args, "refresh", defaultValue: true);
        var scope = OptString(args, "scope") ?? "syntax";
        if (CsxBufferDiagnostics.IsCsxPath(buf.Path)
            && (scope is "syntax" or "csx" or "script" or "parse" or "file"))
            scope = CsxBufferDiagnostics.Scope;

        if (!force
            && (!refresh || (buf.LastDiagnosedVersion == buf.Version
                && string.Equals(buf.LastDiagnosedScope ?? "syntax", scope, StringComparison.OrdinalIgnoreCase)))
            && buf.LastDiagnosticsJson is { Length: > 0 })
        {
            return JsonSerializer.Serialize(new
            {
                schema = "doc_diagnostics/v0",
                ok = true,
                cached = true,
                scope = buf.LastDiagnosedScope ?? scope,
                meta = buf.ToMeta(),
                diagnostics = ResponseCaps.CapDiagnostics(
                    JsonSerializer.Deserialize<JsonElement>(buf.LastDiagnosticsJson))
            }, Pretty);
        }

        if (force)
            buf.LastDiagnosedVersion = null;

        var (diagnostics, diagNote) = await TryDiagnoseScopedAsync(
                buf, store, session, byDomain, flush, scope, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Serialize(new
        {
            schema = "doc_diagnostics/v0",
            ok = true,
            cached = diagNote?.Contains("cache_hit", StringComparison.Ordinal) == true,
            scope,
            meta = buf.ToMeta(),
            diagnostics,
            diagnostics_note = diagNote
        }, Pretty);
    }

    static async Task<(object? diagnostics, string? note)> TryDiagnoseScopedAsync(
        DocBuffer buf,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        bool flush,
        string scope,
        CancellationToken cancellationToken)
    {
        if (flush && buf.Dirty)
            store.Flush(buf);

        if (buf.Dirty)
            return (null, "Buffer is dirty and flush=false — diagnostics would be stale; flush first.");

        var lang = buf.Language;
        if (lang is not "csharp" and not "typescript")
            return (null, $"No online diagnostics for language '{lang}' (csharp|typescript only).");

        // .csx: ScriptHost allowlist — not ParseText/MSBuild (closes green-buffer / red-check).
        if (CsxBufferDiagnostics.IsCsxPath(buf.Path))
        {
            var csxScope = string.Equals(scope, "syntax", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "csx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "script", StringComparison.OrdinalIgnoreCase)
                ? CsxBufferDiagnostics.Scope
                : scope;

            if (buf.LastDiagnosticsJson is { Length: > 0 }
                && buf.LastDiagnosedVersion == buf.Version
                && string.Equals(buf.LastDiagnosedScope ?? CsxBufferDiagnostics.Scope, csxScope, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var hit = JsonSerializer.Deserialize<JsonElement>(buf.LastDiagnosticsJson);
                    return (ResponseCaps.CapDiagnostics(hit), "cache_hit (unchanged buffer version)");
                }
                catch
                {
                    // recompute
                }
            }

            try
            {
                var raw = await CsxBufferDiagnostics.DiagnoseAsync(
                        buf.Path,
                        buf.Text,
                        session.ProjectRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                buf.LastDiagnosticsJson = raw;
                buf.LastDiagnosedUtc = DateTime.UtcNow;
                buf.LastDiagnosedVersion = buf.Version;
                buf.LastDiagnosedScope = CsxBufferDiagnostics.Scope;
                var el = JsonSerializer.Deserialize<JsonElement>(raw);
                return (ResponseCaps.CapDiagnostics(el), null);
            }
            catch (Exception ex)
            {
                return (null, $"csx diagnostics failed: {ex.Message}");
            }
        }

        if (buf.LastDiagnosticsJson is { Length: > 0 }
            && buf.LastDiagnosedVersion == buf.Version
            && string.Equals(buf.LastDiagnosedScope ?? "syntax", scope, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var hit = JsonSerializer.Deserialize<JsonElement>(buf.LastDiagnosticsJson);
                return (ResponseCaps.CapDiagnostics(hit), "cache_hit (unchanged buffer version)");
            }
            catch
            {
                // recompute
            }
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["file_path"] = JsonSerializer.SerializeToElement(buf.Path),
            ["language"] = JsonSerializer.SerializeToElement(lang),
            ["scope"] = JsonSerializer.SerializeToElement(scope),
            ["source_text"] = JsonSerializer.SerializeToElement(buf.Text)
        };
        if (session.SolutionOrProjectPath is { Length: > 0 } sol)
            args["solution_or_project_path"] = JsonSerializer.SerializeToElement(sol);

        try
        {
            var raw = await IdeLanguageTools.DispatchBareAsync(
                "get_diagnostics", session, byDomain, args, cancellationToken).ConfigureAwait(false);
            buf.LastDiagnosticsJson = raw;
            buf.LastDiagnosedUtc = DateTime.UtcNow;
            buf.LastDiagnosedVersion = buf.Version;
            buf.LastDiagnosedScope = scope;
            var el = JsonSerializer.Deserialize<JsonElement>(raw);
            return (ResponseCaps.CapDiagnostics(el), null);
        }
        catch (Exception ex)
        {
            return (null, $"diagnostics failed: {ex.Message}");
        }
    }

    static string Close(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var pathArg = OptString(args, "path");
        var resolved = pathArg is { Length: > 0 } ? ResolveUserPath(session, pathArg) : null;
        var buf = store.Resolve(resolved, OptString(args, "doc_id"));
        // Instant Save: close defaults to flush=true (same as edit). Discard needs explicit discard=true.
        var flush = BoolOr(args, "flush", defaultValue: true);
        var discard = BoolOr(args, "discard", defaultValue: false);
        var wasDirty = buf.Dirty;
        var flushed = false;
        if (wasDirty)
        {
            if (flush)
            {
                store.Flush(buf, allowShrink: true);
                flushed = true;
            }
            else if (!discard)
            {
                throw new InvalidOperationException(
                    $"Buffer is dirty ({buf.Path}). close defaults to flush=true (Instant Save); " +
                    "pass flush=false&discard=true to drop unsaved edits.");
            }
        }

        var path = buf.Path;
        var id = buf.DocId;
        store.Close(path, null);
        return JsonSerializer.Serialize(new
        {
            schema = "doc_close/v0",
            ok = true,
            closed_doc_id = id,
            path,
            flushed,
            discarded_dirty = discard && wasDirty && !flushed
        }, Pretty);
    }

    static Task<(object? diagnostics, string? note)> TryDiagnoseAsync(
        DocBuffer buf,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        bool flush,
        CancellationToken cancellationToken) =>
        TryDiagnoseScopedAsync(buf, store, session, byDomain, flush, scope: "syntax", cancellationToken);

    static string RequireString(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.GetString() is not { Length: > 0 } s)
            throw new ArgumentException($"{key} (string) is required.");
        return s;
    }

    static string? OptString(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) ? el.GetString() : null;

    static int RequireInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || !el.TryGetInt32(out var n))
            throw new ArgumentException($"{key} (integer) is required.");
        return n;
    }

    static int? IntOrNull(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var n) ? n : null;

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
