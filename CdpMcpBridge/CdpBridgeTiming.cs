namespace CdpMcpBridge;

/// <summary>Bridge-side timeouts for deploy gap survival (ADR-0203) — from TOML [bridge].</summary>
internal sealed class CdpBridgeTiming
{
    internal TimeSpan DeployWaitTimeout { get; init; } = TimeSpan.FromMinutes(3);
    internal TimeSpan DeployPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    internal TimeSpan DeployGapRetryInterval { get; init; } = TimeSpan.FromMilliseconds(750);
    internal TimeSpan ServiceReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    internal static CdpBridgeTiming Resolve() =>
        new()
        {
            DeployWaitTimeout = TimeSpan.FromMilliseconds(Cdp.Config.CdpOpsConfig.Current.BridgeDeployWaitMs),
            DeployPollInterval = TimeSpan.FromMilliseconds(Cdp.Config.CdpOpsConfig.Current.BridgeDeployPollMs),
            DeployGapRetryInterval = TimeSpan.FromMilliseconds(Cdp.Config.CdpOpsConfig.Current.BridgeDeployGapRetryMs),
            ServiceReadyTimeout = TimeSpan.FromMilliseconds(Cdp.Config.CdpOpsConfig.Current.BridgeServiceReadyMs),
        };
}
