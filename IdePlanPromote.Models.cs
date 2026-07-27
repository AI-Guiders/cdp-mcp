#nullable enable

namespace CdpMcp;

internal static partial class IdePlanPromote
{
    public sealed record PlanStatus(
        string Schema,
        string PlanId,
        string Status,
        string Path,
        string? Feature,
        Guid? FeatureId,
        Guid? TaskId,
        string? Task,
        DateTime PromotedUtc,
        DateTime? ResolvedUtc,
        string? Notes);
}
