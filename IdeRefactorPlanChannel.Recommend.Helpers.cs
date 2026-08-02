#nullable enable
using System.Text.RegularExpressions;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeRefactorPlanChannel
{
    static bool PreferMethodCut(Hotspot? fileHit, Hotspot methodHit, int fileLines, QualityGates.QualityPolicy policy)
    {
        // After peel: file may be under fail while method_lines still screams — prefer method.
        if (methodHit.Severity == "fail")
            return true;
        if (fileHit is null)
            return true;
        if (policy.FileLinesFail > 0 && fileLines < policy.FileLinesFail)
            return true;
        if (fileHit.Severity == "warn" && methodHit.Value >= policy.MethodLinesWarn)
            return true;
        return false;
    }

    static (string Kind, string? TypeStem) DetectShape(string path)
    {
        // Only C# sources get class/top-level shape — .csproj/.xml/.md are not peel targets.
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return ("non_csharp", Path.GetFileNameWithoutExtension(path));

        try
        {
            var text = File.ReadAllText(path);
            var partial = RxPartialClass.Match(text);
            if (partial.Success)
                return ("partial_class", partial.Groups[1].Value);
            var type = RxClass.Match(text);
            if (type.Success)
                return ("class", type.Groups[1].Value);
            var other = RxOtherType.Match(text);
            if (other.Success)
                return ("type_other", other.Groups[1].Value);
            return ("top_level_statements", Path.GetFileNameWithoutExtension(path));
        }
        catch
        {
            return ("unknown", Path.GetFileNameWithoutExtension(path));
        }
    }

    static string? SanitizeTopic(string? symbol)
    {
        if (symbol is null or { Length: 0 })
            return null;
        var s = symbol;
        var tick = s.IndexOf('`');
        if (tick > 0)
            s = s[..tick];
        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1)
            s = s[(dot + 1)..];
        if (s.EndsWith("Async", StringComparison.Ordinal) && s.Length > 5)
            s = s[..^5];
        var safe = new string (s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return safe.Length > 0 ? safe : null;
    }

    static string? GuessTopicFromFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    static object LeaveRecommend(
        string rel,
        (string Kind, string? TypeStem) shape,
        string why,
        string pulse)
    {
        var primaryGo = new { go = "sa_desk", label = "SA pulse", why = $"path={rel}" };
        return new
        {
            ok = true,
            pulse,
            verdict = "leave",
            why,
            shape = new { kind = shape.Kind, type_stem = shape.TypeStem, path = rel },
            cut = new
            {
                kind = "none",
                cheap = true,
                path = rel,
                steps = new[] { "Optional: go=sa_desk for dirty/clones; skip structural cut." },
                primary_go = primaryGo
            },
            next = new object[]
            {
                primaryGo,
                new { go = "scope", label = "Sniper corridor", why = "aim before extract" },
                new { go = "sa_desk", label = "SA leave|touch|split", why = "confirm verdict" }
            },
            hint = "One package — act on cut.primary_go; skip hand-running debt→budget→partials unless you need detail."
        };
    }
}