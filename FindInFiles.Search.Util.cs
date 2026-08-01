using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Path/arg helpers + Hit for Find in Files Search (≤ADX soft-warn peel).</summary>
internal static partial class FindInFiles
{
    static string ExpandPath(string raw)
    {
        var s = raw.Trim().Trim('"');
        if (s.StartsWith("~/", StringComparison.Ordinal) || s.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            s = Path.Combine(home, s[2..]);
        }
        else if (s == "~")
        {
            s = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.GetFullPath(s);
    }

    static bool IsVolumeRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return root is { Length: > 0 } &&
                   full.Equals(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static string FileLabel(SessionContext session, string absolutePath)
    {
        var root = session.ProjectRoot;
        if (root is { Length: > 0 })
        {
            var fullRoot = Path.GetFullPath(root);
            var full = Path.GetFullPath(absolutePath);
            if (full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[fullRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (rel.Length > 0)
                    return rel.Replace('\\', '/');
            }
        }

        // Outside project: keep rooted path so anchors stay unique (F: value may contain drive ':').
        return Path.GetFullPath(absolutePath).Replace('\\', '/');
    }

    static List<string> SplitLines(string text)
    {
        var list = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            list.Add(line);
        return list;
    }

    static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static bool BoolOr(IReadOnlyDictionary<string, JsonElement> args, string key, bool defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    static int IntOr(IReadOnlyDictionary<string, JsonElement> args, string key, int defaultValue)
    {
        if (!args.TryGetValue(key, out var el))
            return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => defaultValue
        };
    }

    sealed record Hit(string Anchor, string AbsolutePath, int Line, int Column, string Preview);
}
