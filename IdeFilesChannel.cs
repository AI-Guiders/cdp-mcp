#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=files_desk</c> / Meta <c>cdp_files</c> — agent-native File Manager (ADR-0016).
/// Utility (not project-bound): <c>where=project|external|cwd</c> parity with search.
/// </summary>
internal static partial class IdeFilesChannel
{
    public const string SchemaVersion = "files/v1";
    public const string ToolName = "cdp_files";
    public const string CwdKey = "files.cwd";
    public const int SlimCap = 24;
    public const int ListCap = 120;
    public const int TreeDefaultDepth = 2;

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

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
        args = FlattenGoArgs(args);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "list" or "ls" or "dir" => List(session, args),
            "cd" or "chdir" => Cd(session, args),
            "up" or ".." or "cdup" => Up(session),
            "stat" or "info" => Stat(session, args),
            "tree" => Tree(session, args),
            "open" => OpenFile(store, session, args),
            "text" or "dump" or "read" => TextProject(session, args),
            "search" or "find" => SearchFacet(session, args),
            "roots" => Roots(session),
            "clear" => ClearCwd(),
            _ => Scene(session, args)
        };
    }

    static object Scene(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var entries = Enumerate(cwd, args, SlimCap, out var total, out var truncated);
        return Board(
            op: "scene",
            where: where,
            cwd: cwd,
            shape: "slim",
            entries: entries,
            total: total,
            truncated: truncated,
            hint: "Utility FM — where=project|external|cwd. Prefer over shell ls. op=list|cd|up|stat|tree|open|text|search.");
    }

    static object List(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var shape = NormShape(Opt(args, "shape"));
        var cap = shape == "list" ? ListCap : SlimCap;
        var cwd = ResolveCwd(session, args, out var where);
        var entries = Enumerate(cwd, args, cap, out var total, out var truncated);
        return Board(
            op: "list",
            where: where,
            cwd: cwd,
            shape: shape,
            entries: entries,
            total: total,
            truncated: truncated,
            hint: truncated ? $"capped {cap}/{total} — shape=list or filter=" : "cd name=|path= · open path= · search query=");
    }

    static object Cd(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var target = Opt(args, "path") ?? Opt(args, "name") ?? Opt(args, "to") ?? Opt(args, "dir");
        if (string.IsNullOrWhiteSpace(target))
            return Err("path_required", "cd path= or name= (relative to cwd or absolute for external)");

        var cwd = ResolveCwd(session, args, out _);
        string next;
        try
        {
            next = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (!Directory.Exists(next))
            return Err("not_a_directory", next);

        SetCwd(next);
        var entries = Enumerate(next, args, SlimCap, out var total, out var truncated);
        return Board(
            op: "cd",
            where: ClassifyWhere(session, next),
            cwd: next,
            shape: "slim",
            entries: entries,
            total: total,
            truncated: truncated,
            hint: "cwd sticky (files.cwd). up · list · open · search.");
    }

    static object Up(SessionContext session)
    {
        var cwd = GetCwd(session);
        var parent = Directory.GetParent(cwd)?.FullName;
        if (parent is null)
            return Err("at_root", cwd);

        SetCwd(parent);
        var entries = Enumerate(parent, EmptyArgs(), SlimCap, out var total, out var truncated);
        return Board(
            op: "up",
            where: ClassifyWhere(session, parent),
            cwd: parent,
            shape: "slim",
            entries: entries,
            total: total,
            truncated: truncated,
            hint: null);
    }

    static object Stat(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var target = Opt(args, "path") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(target))
            return Err("path_required", "stat path=");

        var cwd = ResolveCwd(session, args, out _);
        string full;
        try
        {
            full = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (Directory.Exists(full))
        {
            var di = new DirectoryInfo(full);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = "files_desk",
                tool = ToolName,
                op = "stat",
                pulse = $"files · dir · {di.Name}",
                entry = EntryFrom(di),
                next = Next(full),
                hint = "cd path= · tree depth=2 · open only for files"
            };
        }

        if (File.Exists(full))
        {
            var fi = new FileInfo(full);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = "files_desk",
                tool = ToolName,
                op = "stat",
                pulse = $"files · file · {fi.Name}",
                entry = EntryFrom(fi),
                next = new object[]
                {
                    new { go = "files_desk", label = "Open", why = $"op=open path={full}" },
                    new { go = "files_desk", label = "Parent", why = "op=cd path=.." }
                },
                hint = "open → cdp_buffer"
            };
        }

        return Err("not_found", full);
    }

    static object Tree(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var depth = 2;
        if (args.TryGetValue("depth", out var dEl) && dEl.TryGetInt32(out var d))
            depth = Math.Clamp(d, 1, 4);
        else
            depth = TreeDefaultDepth;

        var nodes = new List<object>();
        var truncated = false;
        WalkTree(cwd, depth, 0, nodes, ref truncated, maxNodes: ListCap);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            tool = ToolName,
            op = "tree",
            where,
            cwd,
            depth,
            pulse = $"files · tree d={depth} · {ShortPath(cwd)}",
            nodes,
            truncated,
            next = Next(cwd),
            hint = truncated ? "tree capped — lower depth or cd into subtree" : "cd into a folder for focused list"
        };
    }

    static object OpenFile(
        DocumentBufferStore store,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var target = Opt(args, "path") ?? Opt(args, "name");
        if (string.IsNullOrWhiteSpace(target))
            return Err("path_required", "open path=");

        var cwd = ResolveCwd(session, args, out _);
        string full;
        try
        {
            full = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (Directory.Exists(full))
        {
            SetCwd(full);
            return Cd(session, Dict(("path", full)));
        }

        if (!File.Exists(full))
            return Err("not_found", full);

        var asMode = (Opt(args, "as") ?? Opt(args, "mode") ?? "").Trim().ToLowerInvariant();
        if (asMode is not ("buffer" or "edit") && IsTextProjectable(full))
            return TextProject(session, Dict(("path", full), ("max_chars", Opt(args, "max_chars") ?? "")));

        try
        {
            var buf = store.Open(full);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                go = "files_desk",
                tool = ToolName,
                op = "open",
                pulse = $"files · open · {Path.GetFileName(full)}",
                path = full,
                doc_id = buf.DocId,
                next = new object[]
                {
                    new { go = "editor_scene", label = "Editor", why = "buffer open" },
                    new { go = "files_desk", label = "Text dump", why = $"op=text path={full}" },
                    new { go = "files_desk", label = "Cwd list", why = "op=list" }
                },
                hint = "Opened into cdp_buffer — edit via buffer plane; op=text for lynx-like dump"
            };
        }
        catch (Exception ex)
        {
            return Err("open_failed", ex.Message);
        }
    }

    static object SearchFacet(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var query = Opt(args, "query") ?? Opt(args, "q") ?? Opt(args, "text");
        var findWhere = where == "project" ? "project" : "external";
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            tool = ToolName,
            op = "search",
            pulse = query is { Length: > 0 }
                ? $"files · search → find_desk · {query}"
                : "files · search facet",
            cwd,
            where,
            query,
            next = new object[]
            {
                new
                {
                    go = "find_desk",
                    label = "Run find",
                    why = query is { Length: > 0 }
                        ? $"op=run what=text where={findWhere} path={cwd} query={query}"
                        : $"op=run what=text where={findWhere} path={cwd} query="
                },
                new { go = "files_desk", label = "List cwd", why = "op=list" }
            },
            hint = "FM search facet delegates to cdp_search / go=find_desk (ADR-0016). Pass query=."
        };
    }

    static object Roots(SessionContext session)
    {
        var list = new List<object>();
        if (session.ProjectRoot is { Length: > 0 } pr && Directory.Exists(pr))
            list.Add(new { kind = "project", path = Path.GetFullPath(pr) });
        var cwd = GetCwd(session);
        list.Add(new { kind = "cwd", path = cwd });
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady).Take(8))
            list.Add(new { kind = "drive", path = drive.RootDirectory.FullName, label = drive.Name });

        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            tool = ToolName,
            op = "roots",
            pulse = $"files · roots ×{list.Count}",
            roots = list,
            next = new object[]
            {
                new { go = "files_desk", label = "Cd project", why = "op=cd where=project" },
                new { go = "files_desk", label = "Cd external", why = "op=cd where=external path=" }
            },
            hint = "external first-class — cd path=D:\\… without cdp_open"
        };
    }

    static object ClearCwd()
    {
        IdeSettingsStore.Unset(CwdKey);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            go = "files_desk",
            op = "clear",
            pulse = "files · cwd cleared",
            hint = "Next scene uses project_root or process cwd"
        };
    }

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
            pulse = $"files · {where} · {ShortPath(cwd)} · {total}",
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

    static string ResolveCwd(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args,
        out string where)
    {
        var whereRaw = (Opt(args, "where") ?? "").Trim().ToLowerInvariant();
        var pathArg = Opt(args, "path") ?? Opt(args, "root");

        if (whereRaw is "external" || (pathArg is { Length: > 0 } && Path.IsPathRooted(pathArg)))
        {
            where = "external";
            if (pathArg is { Length: > 0 })
            {
                var full = Path.GetFullPath(pathArg);
                if (Directory.Exists(full))
                {
                    SetCwd(full);
                    return full;
                }

                if (File.Exists(full))
                {
                    var parent = Path.GetDirectoryName(full)!;
                    SetCwd(parent);
                    return parent;
                }
            }

            return GetCwd(session);
        }

        if (whereRaw is "project" || whereRaw is "session")
        {
            where = "project";
            if (session.ProjectRoot is { Length: > 0 } pr && Directory.Exists(pr))
            {
                var full = Path.GetFullPath(pr);
                SetCwd(full);
                return full;
            }

            where = "cwd";
            return GetCwd(session);
        }

        // default: sticky cwd, else project, else process
        var sticky = IdeSettingsStore.GetOrNull(CwdKey);
        if (sticky is { Length: > 0 } && Directory.Exists(sticky))
        {
            where = ClassifyWhere(session, sticky);
            return Path.GetFullPath(sticky);
        }

        if (session.ProjectRoot is { Length: > 0 } proj && Directory.Exists(proj))
        {
            where = "project";
            var full = Path.GetFullPath(proj);
            SetCwd(full);
            return full;
        }

        where = "cwd";
        return GetCwd(session);
    }

    static string GetCwd(SessionContext session)
    {
        var sticky = IdeSettingsStore.GetOrNull(CwdKey);
        if (sticky is { Length: > 0 } && Directory.Exists(sticky))
            return Path.GetFullPath(sticky);
        if (session.ProjectRoot is { Length: > 0 } pr && Directory.Exists(pr))
            return Path.GetFullPath(pr);
        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    static void SetCwd(string full) => IdeSettingsStore.Set(CwdKey, Path.GetFullPath(full));

    static string ClassifyWhere(SessionContext session, string path)
    {
        if (session.ProjectRoot is { Length: > 0 } pr)
        {
            try
            {
                var root = Path.GetFullPath(pr);
                var full = Path.GetFullPath(path);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return "project";
            }
            catch
            {
                // fall through
            }
        }

        return "external";
    }

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
