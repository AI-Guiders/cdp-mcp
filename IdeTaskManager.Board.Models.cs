#nullable enable

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    public readonly record struct Board(string Pulse, object View, object Focus);

    public sealed record StageNode(
        Guid Id,
        Guid? ParentId,
        string Title,
        string Status,
        int Ordinal,
        string? PhaseAffinity = null,
        DateTimeOffset? StartedUtc = null,
        DateTimeOffset? CompletedUtc = null);

    public sealed record FeatureNode(
        Guid Id,
        string Title,
        bool IsActive,
        Guid? ActiveStageId,
        IReadOnlyList<StageNode> Stages);

    public sealed record Snapshot(
        Guid? ActiveFeatureId,
        string? ActiveFeatureTitle,
        Guid? ActiveStageId,
        string? ActiveStageTitle,
        string? ActiveStagePhaseAffinity,
        DateTimeOffset? ActiveStageStartedUtc,
        DateTimeOffset? ActiveStageCompletedUtc,
        IReadOnlyList<FeatureNode> Features);
}
