#nullable enable
using Cdp.Deploy;

namespace Cdp.Config;

/// <summary>ADR-0222 canonical config path resolution — bridge <c>--config</c> is SSOT.</summary>
public static class CdpConfigPaths
{
    public const string DefaultBridgeSeat = @"D:\cdp-mcp";

    public static string DefaultBridgeConfigPath =>
        Path.Combine(DefaultBridgeSeat, CdpDeploySeatConfig.FileName);

    /// <summary>Bridge MCP seat config (--config arg or default seat path).</summary>
    public static string ResolveBridgeConfigPath(string? explicitConfig = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfig))
            return Path.GetFullPath(explicitConfig.Trim());

        return DefaultBridgeConfigPath;
    }

    /// <summary>Config for CdpService — explicit path, else bridge SSOT, else seat-local fallback.</summary>
    public static string ResolveServiceConfigPath(string? explicitConfig, string serviceInstallRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfig) && File.Exists(explicitConfig))
            return Path.GetFullPath(explicitConfig.Trim());

        var bridge = ResolveBridgeConfigPath();
        if (File.Exists(bridge))
            return bridge;

        var seat = CdpDeploySeatConfig.ResolveSeatConfigPath(serviceInstallRoot);
        if (seat is not null)
            return seat;

        throw new FileNotFoundException(
            "cdp-mcp.toml not found — pass --config or seat cdp-mcp.toml.",
            CdpDeploySeatConfig.SeatConfigPath(serviceInstallRoot));
    }
}
