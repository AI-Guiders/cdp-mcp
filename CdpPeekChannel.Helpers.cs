#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class CdpPeekChannel
{
    static string? ResolvePath(
        SessionContext session,
        LanguageRegistry langs,
        string path,
        string? scope,
        bool bind,
        out string? bindNote,
        out string? error)
    {
        bindNote = null;
        error = null;
        var p = path.Trim();
        var external = FindInFiles.IsExternalScope(scope) || Path.IsPathRooted(p);

        try
        {
            string abs;
            if (Path.IsPathRooted(p))
            {
                abs = Path.GetFullPath(p);
            }
            else if (session.ProjectRoot is { Length: > 0 } root)
            {
                abs = Path.GetFullPath(Path.Combine(root, p));
            }
            else if (external)
            {
                error = "path_not_rooted";
                return null;
            }
            else
            {
                abs = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), p));
            }

            if (bind && session.ProjectRoot is not { Length: > 0 })
            {
                TryLazyBind(session, langs, abs);
                if (session.ProjectRoot is { Length: > 0 })
                    bindNote = $"lazy_bind root={session.ProjectRoot}";
            }

            if (!File.Exists(abs) && !Directory.Exists(abs))
            {
                error = "not_found";
                return abs;
            }

            return abs;
        }
        catch (Exception ex)
        {
            error = "path_resolve_failed";
            bindNote = ex.Message;
            return null;
        }
    }

    static void TryLazyBind(SessionContext session, LanguageRegistry langs, string absPath)
    {
        try
        {
            var open = langs.Detect(absPath);
            if (open.Root is not { Length: > 0 })
                return;

            session.ProjectRoot = open.Root;
            session.ProjectKind = open.Kind;
            session.Language = CdpLanguages.IsAny(open.Language) ? null : open.Language;
            session.SolutionOrProjectPath = open.SolutionOrProjectPath;
            session.TsConfigPath = open.TsConfigPath;
            session.ScmRoot ??= GitSessionDefaults.TryResolveScmRoot(open.Root);
        }
        catch
        {
            // best-effort
        }
    }

    static bool TryResolveAnchorPath(
        SessionContext session,
        string wire,
        out string? path,
        out string? error)
    {
        path = null;
        error = null;
        try
        {
            var span = BracketLocate.Parse(wire);
            if (span.File is not { Length: > 0 } f)
            {
                error = "anchor_missing_file";
                return false;
            }

            if (Path.IsPathRooted(f))
                path = Path.GetFullPath(f);
            else if (session.ProjectRoot is { Length: > 0 } root)
                path = Path.GetFullPath(Path.Combine(root, f));
            else
                path = Path.GetFullPath(f);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool TryReadPathList(IReadOnlyDictionary<string, JsonElement> args, out List<string> paths)
    {
        paths = new List<string>();
        if (!args.TryGetValue("paths", out var el))
            return false;

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    paths.Add(s);
            }

            return paths.Count > 0;
        }

        if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } one)
        {
            paths.Add(one);
            return true;
        }

        return false;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static int? IntOr(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        if (el.TryGetInt32(out var i))
            return i;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var p))
            return p;
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
