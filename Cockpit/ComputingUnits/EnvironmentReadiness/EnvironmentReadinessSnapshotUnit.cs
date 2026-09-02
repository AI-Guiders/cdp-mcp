#nullable enable

using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness;
using CdpMcp.Cockpit.EnvironmentReadiness;

namespace CdpMcp.Cockpit.ComputingUnits.EnvironmentReadiness;

/// <summary>CCU: Environment Readiness snapshot (ADR 0023/0097 headless).</summary>
internal sealed class EnvironmentReadinessSnapshotUnit
{
    public static EnvironmentReadinessSnapshotUnit Default { get; } = new();

    EnvironmentReadinessSnapshotUnit() { }

    public Task<EnvironmentReadinessSnapshot> BuildAsync(
        EnvironmentReadinessSnapshotBuilder.Input input,
        CancellationToken cancellationToken = default) =>
        EnvironmentReadinessSnapshotBuilder.BuildAsync(input, cancellationToken);
}
