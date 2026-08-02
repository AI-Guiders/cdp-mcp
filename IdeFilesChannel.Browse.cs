#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeFilesChannel
{
    static object List(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var shape = NormShape(Opt(args, "shape"));
        var cap = shape == "list" ? ListCap : SlimCap;
        var cwd = ResolveCwd(session, args, out var where);
        var entries = Enumerate(cwd, args, cap, out var total, out var truncated);
        return Board(op: "list", where: where, cwd: cwd, shape: shape, entries: entries, total: total, truncated: truncated, hint: truncated ? $"capped {cap}/{total} — shape=list or filter=" : "cd name=|path= · open path= · search query=");
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
            next = Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(cwd, target));
        }
        catch (Exception ex)
        {
            return Err("bad_path", ex.Message);
        }

        if (!Directory.Exists(next))
            return Err("not_a_directory", next);
        SetCwd(next);
        var entries = Enumerate(next, args, SlimCap, out var total, out var truncated);
        return Board(op: "cd", where: ClassifyWhere(session, next), cwd: next, shape: "slim", entries: entries, total: total, truncated: truncated, hint: "cwd sticky (files.cwd). up · list · open · search.");
    }

    static object Up(SessionContext session)
    {
        var cwd = GetCwd(session);
        var parent = Directory.GetParent(cwd)?.FullName;
        if (parent is null)
            return Err("at_root", cwd);
        SetCwd(parent);
        var entries = Enumerate(parent, EmptyArgs(), SlimCap, out var total, out var truncated);
        return Board(op: "up", where: ClassifyWhere(session, parent), cwd: parent, shape: "slim", entries: entries, total: total, truncated: truncated, hint: null);
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
            full = Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(cwd, target));
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
                    new
                    {
                        go = "files_desk",
                        label = "Open",
                        why = $"op=open path={full}"},
                    new
                    {
                        go = "files_desk",
                        label = "Parent",
                        why = "op=cd path=.."
                    }
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
                new
                {
                    go = "files_desk",
                    label = "Cd project",
                    why = "op=cd where=project"
                },
                new
                {
                    go = "files_desk",
                    label = "Cd external",
                    why = "op=cd where=external path="
                }
            },
            hint = "external first-class — cd path=D:\\… without cdp_open"
        };
    }

    static object ClearCwd()
    {
        IdeSettingsStore.Unset(CwdKey);
        CideFilesDeskLatch.Publish(active: false, pulse: "files_desk · idle · cwd cleared", op: "clear", where: null, cwd: null, entryCount: 0);
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
}