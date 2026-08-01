using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

/// <summary>
/// Editor plane — git_scene/git_plan isomorphism for buffers (kj-20260724-1640).
/// <c>cdp_editor_scene</c> defaults to desk-parity pulse; <c>detail=full</c> / path|locus|doc_id
/// maps open buffers + optional context; <c>cdp_edit_plan</c>
/// drafts candidates then validate|apply logical slices of buffer edits.
/// </summary>
internal static partial class EditorPlane
{
    public const string SceneSchema = "editor_scene/v0";
    public const string PlanSchema = "edit_plan/v0";
    public const int MaxSlices = 32;
    public const int MaxStepsPerSlice = 64;
    public const int ContextMaxLinesDefault = 80;

    public const string ExampleYaml =
        """
        - message: why this logical edit group
          steps:
            - path: Foo.cs
              edit_op: replace
              old_string: old
              new_string: new
            # or: edit_op: anchor / anchor: "[F:Foo.cs;M:Bar;K:Method]" / text: "…"
        """;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly YamlDotNet.Serialization.IDeserializer Yaml =
        new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static bool IsEditorTool(string name) =>
        name is "cdp_editor_scene" or "cdp_edit_plan" || EditSniper.IsSniperTool(name);

    public static async Task<string> DispatchAsync(
        string name,
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, ICdpBackendModule> byDomain,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken) =>
        name switch
        {
            "cdp_editor_scene" => Scene(store, session, args),
            "cdp_edit_plan" => await PlanAsync(store, session, byDomain, args, cancellationToken)
                .ConfigureAwait(false),
            _ when EditSniper.IsSniperTool(name) => EditSniper.Dispatch(store, session, args),
            _ => throw new ArgumentException($"Unknown editor tool: {name}")
        };

    static string Scene(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var focusPath = OptString(args, "path");
        var focusLocus = OptString(args, "locus") ?? OptString(args, "focus");
        var focusDocId = OptString(args, "doc_id");
        var detail = (OptString(args, "detail") ?? "pulse").Trim().ToLowerInvariant();

        if (focusLocus is { Length: > 0 }
            && focusLocus.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(focusLocus, "buffer:none", StringComparison.OrdinalIgnoreCase))
        {
            focusDocId ??= focusLocus["buffer:".Length..];
        }

        var wantsFull = detail is "full" or "map"
            || focusPath is { Length: > 0 }
            || focusDocId is { Length: > 0 };

        if (!wantsFull)
            return ScenePulse(store, session);

        return SceneFull(store, session, args, focusPath, focusLocus, focusDocId);
    }

    /// <summary>Desk-parity A: counts only — no ProbeDiskChanged / loci dump.</summary>
    static string ScenePulse(DocumentBufferStore store, SessionContext session)
    {
        var docs = store.All;
        var count = 0;
        var dirty = 0;
        foreach (var b in docs)
        {
            count++;
            if (b.Dirty)
                dirty++;
        }

        var pulse = EditorSnapPaneUnit.FormatPulse(new EditorSnapPaneUnit.BufferCounts(count, dirty, 0));
        return JsonSerializer.Serialize(new
        {
            schema = SceneSchema,
            ok = true,
            go = "editor_scene",
            detail = "pulse",
            pulse,
            snap = true,
            session = new
            {
                project_root = session.ProjectRoot,
                language = session.Language,
                solution_or_project_path = session.SolutionOrProjectPath
            },
            count,
            dirty_count = dirty,
            disk_changed_count = 0,
            next = new
            {
                full = "detail=full",
                focus = "path=… | locus=buffer:doc-N"
            },
            hint = "pulse (desk go=editor parity) — detail=full | path= for map + disk probe"
        }, Pretty);
    }

    static string SceneFull(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string? focusPath,
        string? focusLocus,
        string? focusDocId)
    {
        var docs = store.All
            .OrderBy(b => b.DocId, StringComparer.Ordinal)
            .ToArray();

        var drift = 0;
        var loci = new List<object>(docs.Length == 0 ? 1 : docs.Length);
        foreach (var b in docs)
        {
            var changed = b.ProbeMaterialDiskChanged(out _, out var reason);
            if (changed)
                drift++;
            var pulse =
                (changed ? "DISK CHANGED " : "") +
                (b.Dirty ? "DIRTY " : "") +
                ShortPath(b.Path);
            loci.Add(new
            {
                id = $"buffer:{b.DocId}",
                kind = "buffer",
                pulse,
                drill = changed
                    ? "go=reload path=… (or keep_disk) — file modified outside"
                    : "cdp_editor_scene path=… | cdp_edit_plan",
                doc_id = b.DocId,
                path = b.Path,
                dirty = b.Dirty,
                disk_changed = changed,
                disk_changed_reason = reason,
                language = b.Language,
                version = b.Version,
                line_count = CountLines(b.Text),
                diags_cached = b.LastDiagnosedVersion == b.Version && b.LastDiagnosticsJson is { Length: > 0 }
            });
        }

        if (docs.Length == 0)
        {
            loci.Add(new
            {
                id = "buffer:none",
                kind = "buffer",
                pulse = "no open buffers",
                drill = "cdp_buffer op=open",
                count = 0
            });
        }

        object? context = null;
        if (focusPath is { Length: > 0 } || focusDocId is { Length: > 0 })
        {
            try
            {
                var resolved = focusPath is { Length: > 0 } ? ResolveUserPath(session, focusPath) : null;
                var buf = store.Resolve(resolved, focusDocId);
                var maxLines = Math.Clamp(IntOr(args, "context_lines", ContextMaxLinesDefault), 1, 400);
                var start = IntOrNull(args, "start_line") ?? 1;
                var end = IntOrNull(args, "end_line") ?? Math.Min(CountLines(buf.Text), start + maxLines - 1);
                var changed = buf.ProbeDiskChanged(out _, out var reason);
                context = new
                {
                    ok = true,
                    locus = $"buffer:{buf.DocId}",
                    meta = buf.ToMeta(),
                    window = buf.ToReadResult(start, end),
                    disk_changed = changed,
                    disk_changed_reason = reason,
                    diags_note = buf.LastDiagnosedVersion == buf.Version
                        ? "cache_available (cdp_buffer op=diagnostics)"
                        : "stale_or_missing — run cdp_buffer op=diagnostics"
                };
            }
            catch (Exception ex)
            {
                context = new
                {
                    ok = false,
                    path = focusPath,
                    doc_id = focusDocId,
                    locus = focusLocus,
                    error = ex.Message,
                    hint = "Open first: cdp_buffer op=open path=…"
                };
            }
        }

        return JsonSerializer.Serialize(new
        {
            schema = SceneSchema,
            ok = true,
            go = "editor_scene",
            detail = "full",
            session = new
            {
                project_root = session.ProjectRoot,
                language = session.Language,
                solution_or_project_path = session.SolutionOrProjectPath
            },
            count = docs.Length,
            dirty_count = docs.Count(d => d.Dirty),
            disk_changed_count = drift,
            buffers = docs.Select(b => b.ToMeta()).ToArray(),
            loci,
            context,
            next = new
            {
                disk_peek = drift > 0 ? "cdp_buffer op=disk_peek (all drifted) or path=" : null,
                reload = drift > 0 ? "cdp_buffer op=reload (all drifted) or path=" : null,
                keep_disk = drift > 0 ? "cdp_buffer op=keep_disk (all drifted) or path= — Don't Reload" : null,
                draft = "cdp_edit_plan op=draft",
                apply = "cdp_edit_plan op=apply yaml=… (preferred) or slices=[]",
                comfort = "go=undo|redo|copy|cut|paste|put|clipboard|find|back|scratch",
                buffer = "cdp_buffer still fine for single surgical edit",
                pulse = "omit detail= / detail=pulse — desk snap"
            },
            comfort = EditorComfort.Snap(),
            human_focus = NavigationFocusLatch.PeekForScene(),
            hint = drift > 0
                ? "File(s) modified outside — go=disk_peek → reload | keep_disk."
                : "Map first (this tool); multi-step → edit_plan YAML slices (git_plan analogue). " +
                  "Prefer edit_op=anchor [F:;M:;K:]. Comfort: put (dump draft) → refine; copy/cut/paste/clipboard frames."
        }, Pretty);
    }

}
