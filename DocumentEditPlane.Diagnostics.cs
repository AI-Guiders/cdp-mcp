using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class DocumentEditPlane
{
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
