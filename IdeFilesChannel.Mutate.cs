#nullable enable
using System.Collections.Concurrent;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Standard FM mutations for IdeFilesChannel (ADR-0016 utility):
/// new/mkdir/delete/rename/move/copy + file clipboard (clip_copy/clip_cut/clip_paste/clip_clear/clip_show).
/// All paths resolve against cwd unless rooted. Destructive ops (delete/overwrite) require explicit confirm.
/// </summary>
internal static partial class IdeFilesChannel
{
    static readonly ConcurrentDictionary<string, (string[] Paths, bool Cut)> FileClip =
        new(StringComparer.Ordinal);
    const string ClipKey = "default";

    static string ResolveTarget(SessionContext session, IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        var p = Opt(args, key)?.Trim() ?? "";
        if (p.Length == 0) return "";
        return Path.IsPathRooted(p)
            ? Path.GetFullPath(p)
            : Path.GetFullPath(Path.Combine(ResolveCwd(session, args, out _), p));
    }

    static bool Flag(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.True;

    static object Mutation(string op, string where, string cwd, string detail) =>
        Board(op: op, where: where, cwd: cwd, shape: "slim",
            entries: Array.Empty<object>(), total: 0, truncated: false, hint: detail);

    static string UniqueDestination(string dest)
    {
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
        var dir = Path.GetDirectoryName(dest) ?? ".";
        var name = Path.GetFileNameWithoutExtension(dest);
        var ext = Path.GetExtension(dest);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    public static object New(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        var kind = (Opt(args, "kind") ?? "file").Trim().ToLowerInvariant();
        if (path.Length == 0) return Mutation("new", where, cwd, "path required");
        if (File.Exists(path) || Directory.Exists(path))
            return Mutation("new", where, cwd, "already_exists: " + path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (kind is "dir" or "directory")
        {
            Directory.CreateDirectory(path);
            return Mutation("new", where, cwd, "dir created: " + path);
        }

        File.WriteAllText(path, Opt(args, "text") ?? "");
        return Mutation("new", where, cwd, "file created: " + path);
    }

    public static object Mkdir(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        if (path.Length == 0) return Mutation("mkdir", where, cwd, "path required");
        Directory.CreateDirectory(path);
        return Mutation("mkdir", where, cwd, "dir created: " + path);
    }

    public static object Delete(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        if (path.Length == 0) return Mutation("delete", where, cwd, "path required");
        if (!File.Exists(path) && !Directory.Exists(path))
            return Mutation("delete", where, cwd, "not_found: " + path);
        if (!Flag(args, "confirm"))
            return Mutation("delete", where, cwd, "refused: confirm=true required (destructive)");

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
        return Mutation("delete", where, cwd, "deleted: " + path);
    }

    public static object Rename(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        var name = Opt(args, "name")?.Trim() ?? "";
        if (path.Length == 0 || name.Length == 0)
            return Mutation("rename", where, cwd, "path and name required");
        if (!File.Exists(path) && !Directory.Exists(path))
            return Mutation("rename", where, cwd, "not_found: " + path);

        var dest = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (File.Exists(dest) || Directory.Exists(dest))
            return Mutation("rename", where, cwd, "already_exists: " + dest);

        if (Directory.Exists(path)) Directory.Move(path, dest);
        else File.Move(path, dest);
        return Mutation("rename", where, cwd, $"renamed: {path} -> {dest}");
    }

    public static object Move(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        var to = ResolveTarget(session, args, "to");
        if (path.Length == 0 || to.Length == 0)
            return Mutation("move", where, cwd, "path and to required");
        if (!File.Exists(path) && !Directory.Exists(path))
            return Mutation("move", where, cwd, "not_found: " + path);

        var dest = Directory.Exists(to) ? Path.Combine(to, Path.GetFileName(path)) : to;
        dest = UniqueDestination(dest);
        if (Directory.Exists(path)) Directory.Move(path, dest);
        else File.Move(path, dest);
        return Mutation("move", where, cwd, $"moved: {path} -> {dest}");
    }

    public static object Copy(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var path = ResolveTarget(session, args, "path");
        var to = ResolveTarget(session, args, "to");
        if (path.Length == 0 || to.Length == 0)
            return Mutation("copy", where, cwd, "path and to required");
        if (!File.Exists(path) && !Directory.Exists(path))
            return Mutation("copy", where, cwd, "not_found: " + path);

        var dest = Directory.Exists(to) ? Path.Combine(to, Path.GetFileName(path)) : to;
        dest = UniqueDestination(dest);
        if (Directory.Exists(path)) CopyDirectory(path, dest);
        else File.Copy(path, dest);
        return Mutation("copy", where, cwd, $"copied: {path} -> {dest}");
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src))
            CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    public static object ClipSet(SessionContext session, IReadOnlyDictionary<string, JsonElement> args, bool cut)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var paths = new List<string>();

        if (args.TryGetValue("paths", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
            foreach (var el in arrEl.EnumerateArray())
                if (el.ValueKind == JsonValueKind.String)
                    paths.Add(ResolveTarget(session, args, el.GetString() ?? ""));

        if (paths.Count == 0 && Opt(args, "path") is { Length: > 0 })
            paths.Add(ResolveTarget(session, args, "path"));

        if (paths.Count == 0) return Mutation(cut ? "clip_cut" : "clip_copy", where, cwd, "paths required");
        FileClip[ClipKey] = (paths.ToArray(), cut);
        return Mutation(cut ? "clip_cut" : "clip_copy", where, cwd,
            $"clipped ({(cut ? "cut" : "copy")}): {string.Join(", ", paths)}");
    }

    public static object ClipClear()
    {
        FileClip.TryRemove(ClipKey, out _);
        return Mutation("clip_clear", "cwd", "cwd", "clipboard cleared");
    }

    public static object ClipShow()
    {
        var (paths, cut) = FileClip.TryGetValue(ClipKey, out var v) ? v : (Array.Empty<string>(), false);
        return Mutation("clip_show", "cwd", "cwd",
            paths.Length == 0 ? "clipboard empty" : $"clipboard ({(cut ? "cut" : "copy")}): {string.Join(", ", paths)}");
    }

    public static object ClipPaste(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var cwd = ResolveCwd(session, args, out var where);
        var to = ResolveTarget(session, args, "to");
        if (to.Length == 0) to = cwd;
        if (!FileClip.TryGetValue(ClipKey, out var clip) || clip.Paths.Length == 0)
            return Mutation("paste", where, cwd, "clipboard empty");

        Directory.CreateDirectory(to);
        var results = new List<string>();
        foreach (var src in clip.Paths)
        {
            if (!File.Exists(src) && !Directory.Exists(src))
            {
                results.Add("not_found: " + src);
                continue;
            }

            var dest = Path.Combine(to, Path.GetFileName(src));
            dest = UniqueDestination(dest);
            if (clip.Cut)
            {
                if (Directory.Exists(src)) Directory.Move(src, dest);
                else File.Move(src, dest);
                results.Add("moved: " + dest);
            }
            else
            {
                if (Directory.Exists(src)) CopyDirectory(src, dest);
                else File.Copy(src, dest);
                results.Add("copied: " + dest);
            }
        }

        if (clip.Cut) FileClip.TryRemove(ClipKey, out _);
        return Mutation("paste", where, cwd, string.Join("; ", results));
    }
}
