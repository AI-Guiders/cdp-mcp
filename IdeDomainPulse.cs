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

    public static bool TryParse(string path, string text, out DomainCard card)
    {
        card = default!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var idMatch = IdLine.Match(text);
        var id = idMatch.Success
            ? idMatch.Groups["id"].Value
            : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var title = FirstHeading(text) ?? id;
        var invariants = ExtractSectionBullets(text, "Invariants");
        var entry = ExtractSectionBullets(text, "Entry");
        var antipatterns = ExtractSectionBullets(text, "Antipatterns");
        card = new DomainCard(id.Trim(), title.Trim(), invariants, entry, antipatterns);
        return true;
    }

    public static IReadOnlyList<DomainCard> SelectCards(
        IReadOnlyList<DomainCard> cards,
        string? focusHint,
        int maxCards)
    {
        if (cards.Count == 0 || maxCards <= 0)
            return [];

        maxCards = Math.Clamp(maxCards, 1, MaxCardsA);
        var hint = (focusHint ?? "").ToLowerInvariant();
        var scored = cards
            .Select(c => (Card: c, Score: ScoreCard(c, hint)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => PreferredIndex(x.Card.Id))
            .ThenBy(x => x.Card.Id, StringComparer.OrdinalIgnoreCase)
            .Take(maxCards)
            .Select(x => x.Card)
            .ToList();
        return scored;
    }

    static void AppendChainA(StringBuilder sb, DomainCard c)
    {
        sb.Append('[').Append(c.Id).Append("] ").Append(c.Title);
        var n = 0;
        foreach (var inv in c.Invariants)
        {
            if (n++ >= MaxEdgeLinesPerCard)
                break;
            sb.AppendLine();
            sb.Append("  · ").Append(TrimOneLine(inv, 120));
        }

        if (c.Entry.Count > 0)
        {
            sb.AppendLine();
            sb.Append("  → ").Append(TrimOneLine(c.Entry[0], 100));
        }

        if (c.Antipatterns.Count > 0)
        {
            sb.AppendLine();
            sb.Append("  ≠ ").Append(TrimOneLine(c.Antipatterns[0], 100));
        }
    }

    static int ScoreCard(DomainCard c, string hintLower)
    {
        if (hintLower.Length == 0)
            return PreferredOrder.Contains(c.Id, StringComparer.OrdinalIgnoreCase) ? 1 : 0;

        var score = 0;
        if (hintLower.Contains(c.Id, StringComparison.Ordinal))
            score += 10;
        foreach (var token in c.Id.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length >= 2 && hintLower.Contains(token, StringComparison.Ordinal))
                score += 4;
        }

        // Keyword bridges for seeded cards.
        if (c.Id.Equals("tm", StringComparison.OrdinalIgnoreCase)
            && (hintLower.Contains("task", StringComparison.Ordinal)
                || hintLower.Contains("plan", StringComparison.Ordinal)
                || hintLower.Contains("feature", StringComparison.Ordinal)
                || hintLower.Contains("leaf", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("ignite", StringComparison.OrdinalIgnoreCase)
            && (hintLower.Contains("ignite", StringComparison.Ordinal)
                || hintLower.Contains("autoi", StringComparison.Ordinal)
                || hintLower.Contains("remount", StringComparison.Ordinal)
                || hintLower.Contains("wake", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            && (hintLower.Contains("cockpit", StringComparison.Ordinal)
                || hintLower.Contains("desk", StringComparison.Ordinal)
                || hintLower.Contains("seat", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("pressure", StringComparison.OrdinalIgnoreCase)
            && (hintLower.Contains("pressure", StringComparison.Ordinal)
                || hintLower.Contains("compact", StringComparison.Ordinal)
                || hintLower.Contains("stash", StringComparison.Ordinal)))
            score += 6;

        if (hintLower.Contains("domain", StringComparison.Ordinal))
            score += 2;

        return score;
    }

    static int PreferredIndex(string id)
    {
        var i = Array.FindIndex(PreferredOrder, x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 100 : i;
    }

    static string? FirstHeading(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();
        }

        return null;
    }

    static IReadOnlyList<string> ExtractSectionBullets(string text, string section)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;
        var bullets = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var name = line[3..].Trim();
                if (inSection)
                    break;
                inSection = name.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;
            if (line.StartsWith("- ", StringComparison.Ordinal))
                bullets.Add(line[2..].Trim());
        }

        return bullets;
    }

    static string TrimOneLine(string s, int max)
    {
        var t = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }

    /// <summary>Dense memorable form: id/title + sealed edges + entry + antipattern.</summary>
    public sealed record DomainCard(
        string Id,
        string Title,
        IReadOnlyList<string> Invariants,
        IReadOnlyList<string> Entry,
        IReadOnlyList<string> Antipatterns);
}
