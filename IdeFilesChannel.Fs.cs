#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Board/Enumerate/WalkTree/ResolveCwd helpers for IdeFilesChannel (soft-warn peel).</summary>
internal static partial class IdeFilesChannel
{
    static object Board(
        string op,
        string where,
        string cwd,
        string shape,
        IReadOnlyList<object> entries,
        int total,
        bool truncated,
        string? hint)
    {
        var pulse = $"files · {where} · {ShortPath(cwd)} · {total}";
        CideFilesDeskLatch.Publish(
            active: true,
            pulse: pulse,
            op: op,
            where: where,
            cwd: cwd,
            entryCount: total);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "files",
            go = "files_desk",
            tool = ToolName,
            op,
            where,
            cwd,
            shape,
            pulse,
            total,
            truncated,
            entries,
            next = Next(cwd),
            hint
        };
    }

    static object[] Next(string cwd) =>
    [
        new { go = "files_desk", label = "Up", why = "op=up" },
        new { go = "files_desk", label = "List", why = "op=list" },
        new { go = "files_desk", label = "Tree", why = "op=tree depth=2" },
        new { go = "files_desk", label = "Search here", why = $"op=search path={cwd} query=" },
        new { go = "find_desk", label = "Find desk", why = $"where=external path={cwd}" }
    ];

    static List<object> Enumerate(
        string cwd,
        IReadOnlyDictionary<string, JsonElement> args,
        int cap,
        out int total,
        out bool truncated)
    {
        var filter = Opt(args, "filter") ?? Opt(args, "glob");
        var kind = (Opt(args, "kind") ?? "all").Trim().ToLowerInvariant();
        var showHidden = IsTruthy(args, "hidden") || IsTruthy(args, "show_hidden");

        IEnumerable<FileSystemInfo> items;
        try
        {
            var di = new DirectoryInfo(cwd);
            items = di.EnumerateFileSystemInfos()
                .OrderBy(x => x is not DirectoryInfo)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            total = 0;
            truncated = false;
            return [];
        }

        var matched = new List<FileSystemInfo>();
        foreach (var info in items)
        {
            if (!showHidden && IsHidden(info))
                continue;
            if (kind is "file" or "files" && info is not FileInfo)
                continue;
            if (kind is "dir" or "dirs" or "directory" or "folder" && info is not DirectoryInfo)
                continue;
            if (filter is { Length: > 0 } && !MatchFilter(info.Name, filter))
                continue;
            matched.Add(info);
        }

        total = matched.Count;
        truncated = total > cap;
        return matched.Take(cap).Select(EntryFrom).Cast<object>().ToList();
    }

    static void WalkTree(string dir, int maxDepth, int level, List<object> sink, ref bool truncated, int maxNodes)
    {
        if (sink.Count >= maxNodes)
        {
            truncated = true;
            return;
        }

        DirectoryInfo[] dirs;
        FileInfo[] files;
        try
        {
            var di = new DirectoryInfo(dir);
            dirs = di.GetDirectories().Where(d => !IsHidden(d)).OrderBy(d => d.Name).ToArray();
            files = di.GetFiles().Where(f => !IsHidden(f)).OrderBy(f => f.Name).ToArray();
        }
        catch
        {
            return;
        }

        foreach (var d in dirs)
        {
            if (sink.Count >= maxNodes)
            {
                truncated = true;
                return;
            }

            sink.Add(new { depth = level, kind = "dir", name = d.Name, path = d.FullName });
            if (level + 1 < maxDepth)
                WalkTree(d.FullName, maxDepth, level + 1, sink, ref truncated, maxNodes);
        }

        foreach (var f in files)
        {
            if (sink.Count >= maxNodes)
            {
                truncated = true;
                return;
            }

            sink.Add(new { depth = level, kind = "file", name = f.Name, path = f.FullName, size = f.Length });
        }
    }

    static object EntryFrom(FileSystemInfo info) =>
        info switch
        {
            DirectoryInfo d => new
            {
                kind = "dir",
                name = d.Name,
                path = d.FullName,
                mtime_utc = d.LastWriteTimeUtc,
                hidden = IsHidden(d)
            },
            FileInfo f => new
            {
                kind = "file",
                name = f.Name,
                path = f.FullName,
                size = f.Length,
                mtime_utc = f.LastWriteTimeUtc,
                hidden = IsHidden(f)
            },
            _ => new { kind = "other", name = info.Name, path = info.FullName }
        };


    static bool MatchFilter(string name, string filter)
    {
        if (filter.Contains('*') || filter.Contains('?'))
            return MatchesGlob(name, filter);
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    static bool MatchesGlob(string name, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    static bool IsHidden(FileSystemInfo info) =>
        info.Name.StartsWith('.') ||
        (info.Attributes & FileAttributes.Hidden) != 0;

    static string NormShape(string? raw) =>
        (raw ?? "slim").Trim().ToLowerInvariant() switch
        {
            "list" or "full" => "list",
            "raw" => "raw",
            _ => "slim"
        };

    static string ShortPath(string path)
    {
        if (path.Length <= 48)
            return path;
        return "…" + path[^45..];
    }

    static object Err(string code, string hint) => new
    {
        ok = false,
        schema = SchemaVersion,
        go = "files_desk",
        tool = ToolName,
        error = code,
        hint
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => el.GetString() is "1" or "true" or "yes" or "on",
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            _ => false
        };
    }

    static IReadOnlyDictionary<string, JsonElement> FlattenGoArgs(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("go_args", out var ga) || ga.ValueKind != JsonValueKind.Object)
            return args;
        var flat = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
        foreach (var p in ga.EnumerateObject())
        {
            if (!flat.ContainsKey(p.Name))
                flat[p.Name] = p.Value.Clone();
        }

        return flat;
    }

    static Dictionary<string, JsonElement> EmptyArgs() => new(StringComparer.Ordinal);

    static Dictionary<string, JsonElement> Dict(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
