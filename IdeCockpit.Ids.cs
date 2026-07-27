#nullable enable

namespace CdpMcp;

/// <summary>
/// IDS (Ide Display System) — agent-side overlay discoverability (CIDE ADR 0079).
/// Fuzzy desk go-verbs / organs (VS Ctrl+Q analog) — not cabin zone routing (that is CDS).
/// Orthogonal to CDS (ADR 0036); palette-like, not seat slots.
/// </summary>
internal static partial class IdeCockpit
{
    public readonly record struct FeatureHit(string Go, int Score, string Tool);

    /// <summary>VS Ctrl+Q — fuzzy desk verbs / organs (not code).</summary>
    public static FeatureHit[] SearchFeatures(string query, int max)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
            return [];

        static int Score(string name, string query)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 1000;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 800;
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return 500;
            return 0;
        }

        return GoMap.Keys
            .Select(go => (go, score: Score(go, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.go, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => new FeatureHit(x.go, x.score, GoMap[x.go].Tool))
            .ToArray();
    }
}
