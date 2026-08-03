#nullable enable
using System.Text;

namespace CdpMcp;

/// <summary>
/// Standing healthy-agent rules [A] from <c>.cdp/rules/*.md</c> (same card shape as domain).
/// Remount/Autoi appendix + soft organ <c>go=rules</c>. Not eQRH; not Cursor alwaysApply dump.
/// </summary>
internal static class IdeStandingPulse
{
    public const int MaxCardsA = 2;
    public const int MaxChargeChars = 700;

    /// <summary>Test hook — force rules dir.</summary>
    internal static string? DirOverrideForTests { get; set; }

    public static string? ResolveDir(string? projectRoot)
    {
        if (DirOverrideForTests is { Length: > 0 } o)
            return o;

        foreach (var root in CandidateRoots(projectRoot))
        {
            var dir = Path.Combine(root, ".cdp", "rules");
            if (Directory.Exists(dir))
                return dir;
        }

        return projectRoot is { Length: > 0 }
            ? Path.Combine(projectRoot, ".cdp", "rules")
            : null;
    }

    static IEnumerable<string> CandidateRoots(string? projectRoot)
    {
        if (projectRoot is { Length: > 0 })
            yield return projectRoot;

        var stashRoot = IdePressureChannel.TryPeekProjectRoot();
        if (stashRoot is { Length: > 0 }
            && !string.Equals(stashRoot, projectRoot, StringComparison.OrdinalIgnoreCase))
            yield return stashRoot;

        var cwd = Environment.CurrentDirectory;
        if (cwd is { Length: > 0 })
            yield return cwd;

        var baseDir = AppContext.BaseDirectory;
        if (baseDir is { Length: > 0 })
            yield return baseDir;
    }

    public static IReadOnlyList<IdeDomainPulse.DomainCard> LoadCards(string? projectRoot)
    {
        var dir = ResolveDir(projectRoot);
        if (dir is null || !Directory.Exists(dir))
            return [];

        var list = new List<IdeDomainPulse.DomainCard>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(path);
                if (IdeDomainPulse.TryParse(path, text, out var card))
                    list.Add(card);
            }
            catch
            {
                /* best-effort */
            }
        }

        return list;
    }

    public static string FormatPulseA(
        IReadOnlyList<IdeDomainPulse.DomainCard> cards,
        string? focusHint = null,
        int maxCards = MaxCardsA)
    {
        // Prefer healthy-agent when no focus; Domain SelectCards still scores by id tokens.
        var hint = string.IsNullOrWhiteSpace(focusHint) ? "healthy-agent body biped dig" : focusHint;
        var pulse = IdeDomainPulse.FormatPulseA(cards, hint, Math.Min(maxCards, MaxCardsA));
        if (pulse.Length == 0)
            return "";
        return pulse.Length <= MaxChargeChars ? pulse : pulse[..MaxChargeChars].TrimEnd() + "…";
    }

    /// <summary>Appendix for remount / OOM / escalate charge — empty when no cards.</summary>
    public static string RemountStandingAppendix(string? projectRoot = null, string? focusHint = null)
    {
        focusHint ??= IdeDomainPulse.FocusHintFromPlanLatch();
        var pulse = FormatPulseA(LoadCards(projectRoot), focusHint);
        if (pulse.Length == 0)
            return "";
        return "Standing rules [A] (.cdp/rules)\n" + pulse;
    }
}
