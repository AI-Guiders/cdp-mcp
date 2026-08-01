#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=find_desk</c> / Meta <c>cdp_search</c> — agent-native search (ADR-0009).
/// Axes: what / where / shape. Text engine = FindInFiles + buffer find.
/// </summary>
internal static class IdeFindChannel
{
    public const string SchemaVersion = "find_organ/v1";
    public const string ToolName = "cdp_search";
    public const string LastKey = "find.last";
    public const int SlimHitCap = 1;
    public const int ListHitCap = 40;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static string HandleJson(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(store, session, args), Pretty);

    public static object Handle(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? "run").Trim().ToLowerInvariant();

        return op switch
        {
            "last" => LastCard(),
            "clear" => ClearCard(),
            "refine" => Run(store, session, MergeRefine(args), shapeOverride: null),
            "run" or "search" or "find" => Run(store, session, args, shapeOverride: null),
            _ => new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                error = "unknown_op",
                hint = "op=run|refine|last|clear"
            }
        };
    }

    static object Run(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string? shapeOverride)
    {
        var what = (Opt(args, "what") ?? "text").Trim().ToLowerInvariant();
        var where = (Opt(args, "where") ?? Opt(args, "scope") ?? "project").Trim().ToLowerInvariant();
        var shape = (shapeOverride ?? Opt(args, "shape") ?? "slim").Trim().ToLowerInvariant();
        if (shape is not ("slim" or "list" or "raw"))
            shape = "slim";

        if (what is "index" or "fts" or "semantic")
            return StubIndex(where, shape);
        if (what is "symbol" or "symbols" or "ide")
            return StubSymbol(where, shape);

        if (what is not ("text" or "rg" or "grep" or "literal"))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where,
                shape,
                error = "unknown_what",
                hint = "what=text (default) | index | symbol"
            };
        }

        var query = Opt(args, "query") ?? Opt(args, "text") ?? Opt(args, "pattern") ?? Opt(args, "q");
        if (string.IsNullOrEmpty(query))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where,
                shape,
                error = "query_required",
                hint = "query= + where=project|external|dirty|buffers|buffer. shape=slim|list|raw."
            };
        }

        if (!TryBuildFindArgs(store, session, args, where, query!, shape, out var findArgs, out var whereWire, out var pathNote, out var err, out var hint))
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                what,
                where = whereWire,
                shape,
                error = err,
                hint
            };
        }

        SaveLast(what, whereWire, shape, query!, findArgs, pathNote);

        var all = shape is "list" or "raw";
        var rawJson = FindInFiles.IsFilesScope(whereWire) || whereWire is "dirty" or "buffers" or "roots"
            ? FindInFiles.Dispatch(store, session, findArgs, all)
            : EditorComfort.Dispatch(store, session, all ? "find_all" : "find", findArgs);

        return ShapeResult(rawJson, what, whereWire, shape, query!, pathNote);
    }

    static bool TryBuildFindArgs(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        string where,
        string query,
        string shape,
        out Dictionary<string, JsonElement> findArgs,
        out string whereWire,
        out string? pathNote,
        out string error,
        out string hint)
    {
        findArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        whereWire = where;
        pathNote = null;
        error = "";
        hint = "";

        CopyPassthrough(args, findArgs);
        findArgs["query"] = JsonSerializer.SerializeToElement(query);

        var maxDefault = shape is "slim" ? SlimHitCap : ListHitCap;
        if (!findArgs.ContainsKey("max"))
            findArgs["max"] = JsonSerializer.SerializeToElement(maxDefault);

        var exclude = OptList(args, "exclude") ?? OptList(args, "skip");
        var roots = OptList(args, "roots") ?? OptList(args, "paths");
        var pathArg = Opt(args, "path") ?? Opt(args, "search_in") ?? Opt(args, "root");

        switch (where)
        {
            case "buffer":
            case "doc":
            case "file":
                whereWire = "buffer";
                findArgs["scope"] = JsonSerializer.SerializeToElement("buffer");
                if (pathArg is { Length: > 0 })
                    findArgs["path"] = JsonSerializer.SerializeToElement(pathArg);
                return true;

            case "project":
            case "files":
            case "solution":
            case "workspace":
            case "repo":
            case "all":
                whereWire = "project";
                findArgs["scope"] = JsonSerializer.SerializeToElement("project");
                if (roots is { Count: > 0 })
                {
                    var abs = ResolvePaths(session, roots, external: false, out error, out hint);
                    if (abs is null) return false;
                    findArgs["paths"] = JsonSerializer.SerializeToElement(abs);
                    pathNote = $"{abs.Count} roots";
                }
                else if (pathArg is { Length: > 0 })
                {
                    findArgs["path"] = JsonSerializer.SerializeToElement(pathArg);
                }

                return true;

            case "external":
            case "disk":
            case "system":
            case "fs":
            case "anywhere":
                whereWire = "external";
                findArgs["scope"] = JsonSerializer.SerializeToElement("external");
                if (roots is { Count: > 0 })
                {
                    var abs = ResolvePaths(session, roots, external: true, out error, out hint);
                    if (abs is null) return false;
                    findArgs["paths"] = JsonSerializer.SerializeToElement(abs);
                    findArgs["path"] = JsonSerializer.SerializeToElement(abs[0]);
                    pathNote = $"{abs.Count} external roots";
                }
                else if (pathArg is { Length: > 0 })
                {
                    findArgs["path"] = JsonSerializer.SerializeToElement(pathArg);
                }
                else
                {
                    error = "path_required";
                    hint = "where=external needs path= or roots[]";
                    return false;
                }

                return true;

            case "dirty":
            case "scm":
            case "changed":
            {
                whereWire = "dirty";
                var root = session.ProjectRoot;
                if (root is not { Length: > 0 })
                {
                    error = "no_project";
                    hint = "cdp_open first for where=dirty";
                    return false;
                }

                var cards = IdeReviewChannel.ListDirtyFiles(root);
                var files = cards
                    .Select(c => Path.GetFullPath(Path.Combine(root, c.Path.Replace('/', Path.DirectorySeparatorChar))))
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (exclude is { Count: > 0 })
                    files = files.Where(f => !exclude.Any(ex => MatchExclude(f, ex))).ToList();

                if (files.Count == 0)
                {
                    error = "no_dirty_files";
                    hint = "Working tree clean (or no matching files). Try where=project.";
                    return false;
                }

                findArgs["scope"] = JsonSerializer.SerializeToElement("project");
                findArgs["paths"] = JsonSerializer.SerializeToElement(files);
                pathNote = $"{files.Count} dirty files";
                return true;
            }

            case "buffers":
            case "open":
            case "open_buffers":
            {
                whereWire = "buffers";
                var onlyDirty = BoolOr(args, "only_dirty", false);
                var files = store.All
                    .Where(b => !onlyDirty || b.Dirty)
                    .Select(b => b.Path)
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (exclude is { Count: > 0 })
                    files = files.Where(f => !exclude.Any(ex => MatchExclude(f, ex))).ToList();

                if (files.Count == 0)
                {
                    error = "no_open_buffers";
                    hint = "Open files via cdp_buffer first, or where=project.";
                    return false;
                }

                findArgs["scope"] = JsonSerializer.SerializeToElement("project");
                findArgs["paths"] = JsonSerializer.SerializeToElement(files);
                pathNote = $"{files.Count} open buffers";
                return true;
            }

            default:
                error = "unknown_where";
                hint = "where=buffer|project|external|dirty|buffers (+ roots[])";
                return false;
        }
    }

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

    static object LastCard()
    {
        var raw = IdeSettingsStore.GetOrNull(LastKey);
        if (raw is not { Length: > 0 })
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                idle = true,
                pulse = "find · last · idle",
                hint = "No prior cdp_search yet."
            };
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<object>(raw);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                idle = false,
                pulse = "find · last",
                last = parsed,
                hint = "op=refine to replay with exclude[]; op=run with same query."
            };
        }
        catch
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                error = "last_corrupt",
                hint = "op=clear then run again."
            };
        }
    }

    static object ClearCard()
    {
        IdeSettingsStore.Unset(LastKey);
        CideFindDeskLatch.Publish(
            active: false,
            pulse: "find_desk · idle · cleared",
            op: "clear",
            where: null,
            query: null,
            hitCount: 0);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "find",
            go = "find_desk",
            op = "clear",
            pulse = "find · cleared",
            hint = "Last query dropped."
        };
    }

    static Dictionary<string, JsonElement> MergeRefine(IReadOnlyDictionary<string, JsonElement> args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var lastRaw = IdeSettingsStore.GetOrNull(LastKey);
        if (lastRaw is { Length: > 0 })
        {
            try
            {
                using var doc = JsonDocument.Parse(lastRaw);
                var root = doc.RootElement;
                foreach (var name in new[] { "what", "where", "shape", "query", "path", "glob", "regex", "ignore_case", "type" })
                {
                    if (root.TryGetProperty(name, out var el))
                        merged[name] = el.Clone();
                }

                if (root.TryGetProperty("roots", out var rootsEl))
                    merged["roots"] = rootsEl.Clone();
            }
            catch
            {
                // ignore corrupt last
            }
        }

        foreach (var kv in args)
        {
            if (kv.Key is "op") continue;
            merged[kv.Key] = kv.Value;
        }

        if (!merged.ContainsKey("op"))
            merged["op"] = JsonSerializer.SerializeToElement("run");

        return merged;
    }

    static void SaveLast(
        string what,
        string where,
        string shape,
        string query,
        IReadOnlyDictionary<string, JsonElement> findArgs,
        string? pathNote)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["what"] = what,
            ["where"] = where,
            ["shape"] = shape,
            ["query"] = query,
            ["path_note"] = pathNote,
            ["at_utc"] = DateTime.UtcNow.ToString("O")
        };

        if (findArgs.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
            payload["path"] = pathEl.GetString();
        if (findArgs.TryGetValue("glob", out var globEl) && globEl.ValueKind == JsonValueKind.String)
            payload["glob"] = globEl.GetString();
        if (findArgs.TryGetValue("paths", out var pathsEl))
            payload["roots"] = JsonSerializer.Deserialize<object>(pathsEl.GetRawText());

        IdeSettingsStore.Set(LastKey, JsonSerializer.Serialize(payload, Compact));
    }

    static void CopyPassthrough(
        IReadOnlyDictionary<string, JsonElement> src,
        Dictionary<string, JsonElement> dst)
    {
        foreach (var key in new[] { "glob", "g", "type", "filetype", "regex", "ignore_case", "peek", "max", "path", "search_in", "root" })
        {
            if (src.TryGetValue(key, out var el))
                dst[key] = el;
        }
    }

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

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static List<string>? OptList(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;

        if (el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    list.Add(s.Trim());
            }

            return list.Count > 0 ? list : null;
        }

        if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } csv)
        {
            var parts = csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 0 ? parts.ToList() : null;
        }

        return null;
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => defaultValue
        };
    }
}
