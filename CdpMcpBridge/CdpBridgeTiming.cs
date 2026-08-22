namespace CdpMcpBridge;

/// <summary>Bridge-side timeouts for deploy gap survival (ADR-0203).</summary>
internal sealed class CdpBridgeTiming
{
    internal TimeSpan DeployWaitTimeout { get; init; } = TimeSpan.FromMinutes(3);
    internal TimeSpan DeployPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    internal TimeSpan DeployGapRetryInterval { get; init; } = TimeSpan.FromMilliseconds(750);
    internal TimeSpan ServiceReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    internal static CdpBridgeTiming Resolve()
    {
        return new CdpBridgeTiming
        {
            DeployWaitTimeout = ResolveMs("CDP_BRIDGE_DEPLOY_WAIT_MS", 180_000, 5_000, 600_000),
            DeployPollInterval = ResolveMs("CDP_BRIDGE_DEPLOY_POLL_MS", 500, 100, 5_000),
            DeployGapRetryInterval = ResolveMs("CDP_BRIDGE_DEPLOY_GAP_RETRY_MS", 750, 100, 5_000),
            ServiceReadyTimeout = ResolveMs("CDP_BRIDGE_SERVICE_READY_MS", 15_000, 2_000, 120_000)
        };
    }

    static TimeSpan ResolveMs(string env, int defaultMs, int minMs, int maxMs)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable(env), out var ms))
            ms = defaultMs;
        ms = Math.Clamp(ms, minMs, maxMs);
        return TimeSpan.FromMilliseconds(ms);
    }
}
