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

    /// <summary>Bare filename globs (no / * ?) → recursive match under project root.</summary>
    static string? NormalizeFindGlob(string? glob)
    {
        if (glob is not { Length: > 0 })
            return glob;

        var g = glob.Trim();
        if (g.Contains('*') || g.Contains('?') || g.Contains('/') || g.Contains('\\'))
            return g;

        return $"**/{g}";
    }

    static bool ShouldInferFindRegex(string query, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (args.ContainsKey("regex"))
            return false;

        if (query.Contains('|', StringComparison.Ordinal))
            return true;

        return query.Contains('(') && query.Contains(')');
    }

    static string? TryLazyBindForFind(
        SessionContext session,
        LanguageRegistry langs,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!BoolOr(args, "bind", defaultValue: true))
            return null;
        if (session.ProjectRoot is { Length: > 0 })
            return null;

        var pathHint = Opt(args, "path") ?? Opt(args, "search_in") ?? Opt(args, "root");
        if (pathHint is { Length: > 0 })
        {
            var abs = ResolvePath(session, langs, pathHint, Opt(args, "scope"), bind: false, out _, out _);
            if (abs is not null)
            {
                if (Directory.Exists(abs))
                {
                    var candidate = Directory.EnumerateFiles(abs, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
                        ?? Directory.EnumerateFiles(abs, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (candidate is not null)
                        TryLazyBind(session, langs, candidate);
                }
                else if (File.Exists(abs))
                {
                    TryLazyBind(session, langs, abs);
                }
            }
        }

        if (session.ProjectRoot is { Length: > 0 })
            return $"lazy_bind root={session.ProjectRoot}";

        var cwd = Directory.GetCurrentDirectory();
        var csproj = Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (csproj is not null)
        {
            TryLazyBind(session, langs, csproj);
            if (session.ProjectRoot is { Length: > 0 })
                return $"lazy_bind cwd root={session.ProjectRoot}";
        }

        return null;
    }

    static string BuildFindZeroHint(
        string query,
        IReadOnlyDictionary<string, JsonElement> args,
        SessionContext session,
        string? normalizedGlob,
        bool regexApplied)
    {
        var hints = new List<string>();
        if (session.ProjectRoot is not { Length: > 0 })
            hints.Add("no project root — cdp_open or path= to project dir");

        var rawGlob = Opt(args, "glob") ?? Opt(args, "g");
        if (rawGlob is { Length: > 0 } && normalizedGlob is { Length: > 0 } &&
            !string.Equals(rawGlob, normalizedGlob, StringComparison.Ordinal))
        {
            hints.Add($"glob normalized to {normalizedGlob}");
        }

        if (query.Contains('|', StringComparison.Ordinal) && !regexApplied)
            hints.Add("query has | — pass regex=true for alternation");

        if (hints.Count == 0)
            hints.Add("widen query, drop glob=, or scope=external path= for disk-wide rg");

        return string.Join(" · ", hints);
    }
}
