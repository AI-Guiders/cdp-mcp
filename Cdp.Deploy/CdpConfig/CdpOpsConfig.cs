#nullable enable

namespace Cdp.Config;

/// <summary>Process-wide ops config bound from merged TOML (Embedded + operator overlay).</summary>
public sealed class CdpOpsConfig
{
    public string? RgPath { get; init; }
    public string? PluginsRoot { get; init; }
    public string? OpenvsxBase { get; init; }
    public string? OpencodeBin { get; init; }
    public string? OpencodeDirectory { get; init; }
    public string? ForumRoot { get; init; }
    public string? DeployScript { get; init; }
    public int TenantIdleTtlMinutes { get; init; } = 45;
    public bool ExploreCorrEnabled { get; init; } = true;
    public bool ShellIgniteArm { get; init; } = true;
    public bool LifecycleIgniteArm { get; init; } = true;
    public bool ToolWakeArm { get; init; } = true;
    public bool OomWakeCdtEdge { get; init; }
    public string BridgeComposer { get; init; } = "main";
    public int BridgeCapabilitiesPollMs { get; init; } = 2000;
    public int BridgeDeployWaitMs { get; init; } = 180_000;
    public int BridgeDeployPollMs { get; init; } = 500;
    public int BridgeDeployGapRetryMs { get; init; } = 750;
    public int BridgeServiceReadyMs { get; init; } = 15_000;

    public static readonly CdpOpsConfig Default = new();

    public static CdpOpsConfig Current { get; private set; } = Default;

    public static void Bind(CdpConfigDocument doc) =>
        Current = Map(doc);

    /// <summary>Test isolation — restore a snapshot captured before <see cref="Bind"/>.</summary>
    internal static void RestoreForTests(CdpOpsConfig snapshot) =>
        Current = snapshot ?? Default;

    public static CdpOpsConfig Map(CdpConfigDocument doc)
    {
        var bridge = doc.Bridge;
        var ops = doc.Ops;
        return new CdpOpsConfig
        {
            RgPath = Normalize(doc.Tools?.Rg),
            PluginsRoot = Normalize(doc.Tools?.PluginsRoot),
            OpenvsxBase = Normalize(doc.Tools?.OpenvsxBase),
            OpencodeBin = Normalize(doc.Tools?.OpencodeBin),
            OpencodeDirectory = Normalize(doc.Tools?.OpencodeDirectory),
            ForumRoot = Normalize(doc.Forum?.Root),
            DeployScript = Normalize(doc.Deploy?.Script),
            TenantIdleTtlMinutes = Clamp(doc.Tenant?.IdleTtlMinutes, 5, 24 * 60, 45),
            ExploreCorrEnabled = ops?.ExploreCorrEnabled ?? true,
            ShellIgniteArm = ops?.ShellIgniteArm ?? true,
            LifecycleIgniteArm = ops?.LifecycleIgniteArm ?? true,
            ToolWakeArm = ops?.ToolWakeArm ?? true,
            OomWakeCdtEdge = ops?.OomWakeCdtEdge ?? false,
            BridgeComposer = string.IsNullOrWhiteSpace(bridge?.Composer) ? "main" : bridge.Composer.Trim(),
            BridgeCapabilitiesPollMs = Clamp(bridge?.CapabilitiesPollMs, 500, 60_000, 2000),
            BridgeDeployWaitMs = Clamp(bridge?.DeployWaitMs, 5_000, 600_000, 180_000),
            BridgeDeployPollMs = Clamp(bridge?.DeployPollMs, 100, 5_000, 500),
            BridgeDeployGapRetryMs = Clamp(bridge?.DeployGapRetryMs, 100, 5_000, 750),
            BridgeServiceReadyMs = Clamp(bridge?.ServiceReadyMs, 2_000, 120_000, 15_000),
        };
    }

    static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    static int Clamp(int? value, int min, int max, int fallback)
    {
        if (value is not int v)
            return fallback;
        return Math.Clamp(v, min, max);
    }
}
