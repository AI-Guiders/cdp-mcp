#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Ids;

/// <summary>Fuzzy go-verb / organ search (VS Ctrl+Q analog) — not Avalonia.</summary>
public sealed class FeatureSearchUnit : ICockpitComputeUnit, IIdsFeatureSearch
{
    public IdsFeatureHit[] Search(
        string query,
        int max,
        IReadOnlyList<(string Go, string Tool)> catalog)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0 || max <= 0 || catalog.Count == 0)
            return [];

        return catalog
            .Select(entry => (entry.Go, entry.Tool, Score: ScoreName(entry.Go, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Go, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => new IdsFeatureHit(x.Go, x.Score, x.Tool))
            .ToArray();
    }

    static int ScoreName(string name, string query)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 1000;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 800;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 500;
        return 0;
    }
}
