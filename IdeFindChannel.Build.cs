#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Find scope wiring (≤ADX soft-warn peel).</summary>
internal static partial class IdeFindChannel
{
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
