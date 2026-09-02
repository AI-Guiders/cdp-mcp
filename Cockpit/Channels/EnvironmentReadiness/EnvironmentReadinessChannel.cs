#nullable enable

using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness;
using CdpMcp.Cockpit.ComputingUnits.EnvironmentReadiness;
using CdpMcp.Cockpit.EnvironmentReadiness;

namespace CdpMcp.Cockpit.Channels.EnvironmentReadiness;

/// <summary>Environment Readiness channel facade (platform context in, snapshot out).</summary>
internal static class EnvironmentReadinessChannel
{
    public static Task<EnvironmentReadinessSnapshot> BuildAsync(
        EnvironmentReadinessSnapshotBuilder.Input input,
        CancellationToken cancellationToken = default) =>
        EnvironmentReadinessSnapshotUnit.Default.BuildAsync(input, cancellationToken);
}
