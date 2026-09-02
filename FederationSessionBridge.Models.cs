using System.Text.Json.Serialization;

namespace CdpMcp;

internal sealed record FederationGraphPulseDto
{
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    [JsonPropertyName("anchor_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnchorPath { get; init; }

    [JsonPropertyName("phase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phase { get; init; }

    [JsonPropertyName("project_count")]
    public int ProjectCount { get; init; }

    [JsonPropertyName("project_edge_count")]
    public int ProjectEdgeCount { get; init; }

    [JsonPropertyName("file_ownership_count")]
    public int FileOwnershipCount { get; init; }

    [JsonPropertyName("ledger_revision")]
    public long LedgerRevision { get; init; }

    [JsonPropertyName("graph_valid")]
    public bool GraphValid { get; init; }

    [JsonPropertyName("issue_count")]
    public int IssueCount { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
