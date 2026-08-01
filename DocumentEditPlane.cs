using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// CDP document plane: open → structured edit → flush → diagnostics in the same tool result.
/// Almost-online while the agent keeps the turn.
/// </summary>
internal static partial class DocumentEditPlane
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

}
