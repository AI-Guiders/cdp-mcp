#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Shape/Last/ResolvePaths helpers for IdeFindChannel (soft-warn peel).</summary>
internal static partial class IdeFindChannel
{
    static object ShapeResult(
        string rawJson,
        string what,
        string where,
        string shape,
        string query,
        string? pathNote)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        var count = root.TryGetProperty("count", out var cEl) && cEl.TryGetInt32(out var n) ? n : 0;
        var truncated = root.TryGetProperty("truncated", out var tEl) && tEl.ValueKind == JsonValueKind.True;
        object? land = root.TryGetProperty("land", out var landEl) ? JsonSerializer.Deserialize<object>(landEl.GetRawText()) : null;
        object? engineNext = root.TryGetProperty("next", out var nextEl)
            ? JsonSerializer.Deserialize<object>(nextEl.GetRawText())
            : null;

        object? hits = null;
        if (root.TryGetProperty("hits", out var hitsEl) && hitsEl.ValueKind == JsonValueKind.Array)
        {
            var take = shape switch
            {
                "slim" => SlimHitCap,
                "list" => ListHitCap,
                _ => FindInFiles.HardMax
            };
            var list = hitsEl.EnumerateArray().Take(take).Select(h => JsonSerializer.Deserialize<object>(h.GetRawText())).ToArray();
            hits = list;
            if (shape == "slim" && hitsEl.GetArrayLength() > SlimHitCap)
                truncated = true;
        }

        string? engineError = null;
        string? engineHint = null;
        if (!ok)
        {
            engineError = root.TryGetProperty("error", out var eEl) ? eEl.GetString() : "find_failed";
            engineHint = root.TryGetProperty("hint", out var hEl) ? hEl.GetString() : null;
        }

        var pulse = ok
            ? $"find · {where} · {count} hit(s){(truncated ? "+" : "")}{(pathNote is { Length: > 0 } ? $" · {pathNote}" : "")}"
            : $"find · {where} · fail · {engineError}";

        CideFindDeskLatch.Publish(
            active: true,
            pulse: pulse,
            op: "run",
            where: where,
            query: query,
            hitCount: count);

        var next = BuildNext(where, shape, ok, count);

        if (shape == "raw")
        {
            return new
            {
                ok,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                tool = ToolName,
                detail = "raw",
                what,
                where,
                shape,
                query,
                path_note = pathNote,
                pulse,
                count,
                truncated,
                hits,
                land,
                engine = JsonSerializer.Deserialize<object>(rawJson),
                next,
                error = engineError,
                hint = engineHint ?? "shape=slim default; shape=list for capped hits; refine with exclude[]."
            };
        }

        return new
        {
            ok,
            schema = SchemaVersion,
            role = "find",
            go = "find_desk",
            tool = ToolName,
            detail = shape,
            what,
            where,
            shape,
            query,
            path_note = pathNote,
            pulse,
            count,
            truncated,
            hits,
            land,
            next = next ?? engineNext,
            error = engineError,
            hint = engineHint
                ?? (shape == "slim"
                    ? "Slim desk: top hit + land. shape=list for more; op=refine exclude=[]; go=find_desk last."
                    : "List capped. Prefer slim unless you need more loci.")
        };
    }

    static object[] BuildNext(string where, string shape, bool ok, int count) =>
    [
        new { go = "find_desk", label = "Refine", why = "op=refine exclude=[]" },
        new { go = "find_desk", label = shape == "slim" ? "List hits" : "Slim", why = shape == "slim" ? "shape=list" : "shape=slim" },
        new { go = "edit_draft", label = "Edit at land", why = "after land" },
        new { go = "ecl", label = "ECL find-desk", why = "memory checklist" },
        .. (ok && count == 0
            ? new object[] { new { go = "find_desk", label = "Widen", why = where == "dirty" ? "where=project" : "where=external path=" } }
            : Array.Empty<object>())
    ];

    static object StubIndex(string where, string shape) => new
    {
        ok = false,
        schema = SchemaVersion,
        role = "find",
        go = "find_desk",
        what = "index",
        where,
        shape,
        error = "what_index_deferred",
        pulse = "find · index · use codebase_index_search",
        next = new object[]
        {
            new { go = "codebase_index_search", label = "Index search", why = "FTS when indexed" },
            new { go = "find_desk", label = "Text find", why = "what=text" }
        },
        hint = "v0: what=index routes to codebase_index_search (call that tool)."
    };

    static object StubSymbol(string where, string shape) => new
    {
        ok = false,
        schema = SchemaVersion,
        role = "find",
        go = "find_desk",
        what = "symbol",
        where,
        shape,
        error = "what_symbol_deferred",
        pulse = "find · symbol · use find_usages at locus",
        next = new object[]
        {
            new { go = "find_usages", label = "Find usages", why = "at land/locus" },
            new { go = "find_desk", label = "Text find", why = "what=text query=SymbolName" }
        },
        hint = "v0: what=symbol → find_usages / go_to_definition at a locus."
    };


    static List<string>? ResolvePaths(
        SessionContext session,
        IReadOnlyList<string> roots,
        bool external,
        out string error,
        out string hint)
    {
        error = "";
        hint = "";
        var list = new List<string>();
        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = raw.Trim();
            if (!Path.IsPathRooted(p))
            {
                if (external)
                {
                    error = "path_not_rooted";
                    hint = "external roots[] must be absolute";
                    return null;
                }

                var root = session.ProjectRoot;
                if (root is not { Length: > 0 })
                {
                    error = "no_project";
                    hint = "cdp_open or absolute roots[]";
                    return null;
                }

                p = Path.GetFullPath(Path.Combine(root, p));
            }
            else
            {
                p = Path.GetFullPath(p);
            }

            if (!Directory.Exists(p) && !File.Exists(p))
            {
                error = "path_not_found";
                hint = $"roots[] missing: {p}";
                return null;
            }

            list.Add(p);
        }

        if (list.Count == 0)
        {
            error = "roots_empty";
            hint = "roots[] had no usable paths";
            return null;
        }

        return list;
    }

    static bool MatchExclude(string absolutePath, string exclude)
    {
        var norm = absolutePath.Replace('\\', '/');
        var ex = exclude.Replace('\\', '/').Trim();
        if (ex.Length == 0) return false;
        if (ex.Contains('*'))
        {
            // simple substring / suffix
            var token = ex.Trim('*');
            return token.Length > 0 && norm.Contains(token, StringComparison.OrdinalIgnoreCase);
        }

        return norm.Contains(ex, StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(absolutePath).Equals(ex, StringComparison.OrdinalIgnoreCase);
    }

}
