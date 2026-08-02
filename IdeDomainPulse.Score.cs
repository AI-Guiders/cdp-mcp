#nullable enable
using System.Text;
using System.Text.RegularExpressions;

namespace CdpMcp;
internal static partial class IdeDomainPulse
{
    public static bool TryParse(string path, string text, out DomainCard card)
    {
        card = default!;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var idMatch = IdLine.Match(text);
        var id = idMatch.Success ? idMatch.Groups["id"].Value : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(id))
            return false;
        var title = FirstHeading(text) ?? id;
        var invariants = ExtractSectionBullets(text, "Invariants");
        var entry = ExtractSectionBullets(text, "Entry");
        var antipatterns = ExtractSectionBullets(text, "Antipatterns");
        card = new DomainCard(id.Trim(), title.Trim(), invariants, entry, antipatterns);
        return true;
    }

    public static IReadOnlyList<DomainCard> SelectCards(IReadOnlyList<DomainCard> cards, string? focusHint, int maxCards)
    {
        if (cards.Count == 0 || maxCards <= 0)
            return[];
        maxCards = Math.Clamp(maxCards, 1, MaxCardsA);
        var hint = (focusHint ?? "").ToLowerInvariant();
        var scored = cards.Select(c => (Card: c, Score: ScoreCard(c, hint))).OrderByDescending(x => x.Score).ThenBy(x => PreferredIndex(x.Card.Id)).ThenBy(x => x.Card.Id, StringComparer.OrdinalIgnoreCase).Take(maxCards).Select(x => x.Card).ToList();
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
        if (c.Id.Equals("tm", StringComparison.OrdinalIgnoreCase) && (hintLower.Contains("task", StringComparison.Ordinal) || hintLower.Contains("plan", StringComparison.Ordinal) || hintLower.Contains("feature", StringComparison.Ordinal) || hintLower.Contains("leaf", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("ignite", StringComparison.OrdinalIgnoreCase) && (hintLower.Contains("ignite", StringComparison.Ordinal) || hintLower.Contains("autoi", StringComparison.Ordinal) || hintLower.Contains("remount", StringComparison.Ordinal) || hintLower.Contains("wake", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("cockpit", StringComparison.OrdinalIgnoreCase) && (hintLower.Contains("cockpit", StringComparison.Ordinal) || hintLower.Contains("desk", StringComparison.Ordinal) || hintLower.Contains("seat", StringComparison.Ordinal)))
            score += 6;
        if (c.Id.Equals("pressure", StringComparison.OrdinalIgnoreCase) && (hintLower.Contains("pressure", StringComparison.Ordinal) || hintLower.Contains("compact", StringComparison.Ordinal) || hintLower.Contains("stash", StringComparison.Ordinal)))
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
    public sealed record DomainCard(string Id, string Title, IReadOnlyList<string> Invariants, IReadOnlyList<string> Entry, IReadOnlyList<string> Antipatterns);
}