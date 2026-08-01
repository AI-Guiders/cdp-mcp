#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=find_desk</c> / Meta <c>cdp_search</c> — agent-native search (ADR-0009).
/// Axes: what / where / shape. Text engine = FindInFiles + buffer find.
/// </summary>
internal static partial class IdeFindChannel
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
