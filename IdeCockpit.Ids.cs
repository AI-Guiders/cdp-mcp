#nullable enable
using CdpMcp.Cockpit.Ids;

namespace CdpMcp;

/// <summary>
/// IDS peel — feature palette via <see cref="FeatureSearchUnit"/> (CIDE ADR 0079).
/// Orthogonal to CDS (ADR 0036); not Avalonia cabin routing.
/// </summary>
internal static partial class IdeCockpit
{
    static readonly FeatureSearchUnit FeatureSearch = new();

    public readonly record struct FeatureHit(string Go, int Score, string Tool);

    /// <summary>VS Ctrl+Q — fuzzy desk verbs / organs (not code).</summary>
    public static FeatureHit[] SearchFeatures(string query, int max)
    {
        var catalog = GoMap.Keys
            .Select(go => (Go: go, Tool: GoMap[go].Tool))
            .ToArray();
        return FeatureSearch.Search(query, max, catalog)
            .Select(h => new FeatureHit(h.Go, h.Score, h.Tool))
            .ToArray();
    }
}
