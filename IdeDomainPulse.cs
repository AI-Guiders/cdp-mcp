#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;

/// <summary>
/// Domain ownership pulse [A] — reconstruction chains from <c>.cdp/domain/*.md</c>.
/// Used by pressure Domain axis, remount wake appendix, and soft organ <c>go=domain</c>.
/// Chain shape: name → edges (invariants) → entry → antipattern.
/// </summary>
internal static partial class IdeDomainPulse
{
    public const int MaxCardsA = 3;
    public const int MaxEdgeLinesPerCard = 3;
    public const int MaxChargeChars = 900;

    static readonly string[] PreferredOrder = ["tm", "ignite", "cockpit"];

    static readonly Regex IdLine = new(
        @"^\s*-\s*id:\s*`?(?<id>[A-Za-z0-9_.-]+)`?", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Test hook — force domain dir.</summary>
    internal static string? DirOverrideForTests { get; set; }

    public static string? ResolveDir(string? projectRoot)
    {
        if (DirOverrideForTests is { Length: > 0 } o)
            return o;

        foreach (var root in CandidateRoots(projectRoot))
        {
            var dir = Path.Combine(root, ".cdp", "domain");
            if (Directory.Exists(dir))
                return dir;
        }

        return projectRoot is { Length: > 0 }
            ? Path.Combine(projectRoot, ".cdp", "domain")
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

    public static IReadOnlyList<DomainCard> LoadCards(string? projectRoot)
    {
        var dir = ResolveDir(projectRoot);
        if (dir is null || !Directory.Exists(dir))
            return [];

        var list = new List<DomainCard>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(path);
                if (TryParse(path, text, out var card))
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
        IReadOnlyList<DomainCard> cards,
        string? focusHint = null,
        int maxCards = MaxCardsA)
    {
        var picked = SelectCards(cards, focusHint, maxCards);
        if (picked.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var c in picked)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            AppendChainA(sb, c);
        }

        var s = sb.ToString().TrimEnd();
        return s.Length <= MaxChargeChars ? s : s[..MaxChargeChars].TrimEnd() + "…";
    }

    /// <summary>One-card [C] reconstruction chain (full edges + entries + antipatterns).</summary>
    public static string FormatChainC(DomainCard c)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(c.Id).Append("] ").Append(c.Title);
        foreach (var inv in c.Invariants)
        {
            sb.AppendLine();
            sb.Append("  · ").Append(TrimOneLine(inv, 160));
        }

        foreach (var e in c.Entry)
        {
            sb.AppendLine();
            sb.Append("  → ").Append(TrimOneLine(e, 160));
        }

        foreach (var a in c.Antipatterns)
        {
            sb.AppendLine();
            sb.Append("  ≠ ").Append(TrimOneLine(a, 160));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Appendix for remount / Autoi charge — empty when no cards.</summary>
    public static string RemountDomainAppendix(string? projectRoot = null, string? focusHint = null)
    {
        focusHint ??= FocusHintFromPlanLatch();
        var pulse = FormatPulseA(LoadCards(projectRoot), focusHint);
        if (pulse.Length == 0)
            return "";
        return "Domain pulse [A] (.cdp/domain)\n" + pulse;
    }

    public static string? FocusHintFromPlanLatch()
    {
        var latch = CidePlanLatch.TryRead();
        if (latch is null || !latch.Active)
            return null;
        return string.Join(' ', new[] { latch.Feature, latch.Task, latch.Pulse }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

}
