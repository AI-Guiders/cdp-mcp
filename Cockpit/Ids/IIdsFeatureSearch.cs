#nullable enable
namespace CdpMcp.Cockpit.Ids;

/// <summary>IDS feature palette contract (CIDE ADR 0079) — orthogonal to CDS.</summary>
public interface IIdsFeatureSearch
{
    IdsFeatureHit[] Search(string query, int max, IReadOnlyList<(string Go, string Tool)> catalog);
}

public readonly record struct IdsFeatureHit(string Go, int Score, string Tool);
