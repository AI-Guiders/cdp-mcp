namespace Cdp.Deploy;

/// <summary>ADR-0198 install seats — SSOT; service and bridge roots must never alias.</summary>
public sealed record CdpDeployLayout(
    string ServiceInstall,
    string BridgeReleaseInstall,
    string BridgeDebugInstall)
{
    public static CdpDeployLayout Default { get; } = new(
        ServiceInstall: @"D:\cdp-service",
        BridgeReleaseInstall: @"D:\cdp-mcp",
        BridgeDebugInstall: @"D:\cdp-mcp-debug");

    public string StagedService => ServiceInstall + ".next";

    public string StagedBridgeRelease => BridgeReleaseInstall + ".next";

    public string StagedBridgeDebug => BridgeDebugInstall + ".next";

    public string PendingMarker => Path.Combine(ServiceInstall, "cdp-pending-update.json");

    public void ValidateDistinctRoots()
    {
        if (CdpDeployPaths.SamePath(ServiceInstall, BridgeReleaseInstall)
            || CdpDeployPaths.SamePath(ServiceInstall, BridgeDebugInstall)
            || CdpDeployPaths.SamePath(BridgeReleaseInstall, BridgeDebugInstall))
        {
            throw new InvalidOperationException(
                "Deploy layout requires distinct service and bridge install roots (ADR-0198).");
        }
    }

    public string SiblingBridgeForSeat(string seat) =>
        seat switch
        {
            "cdp" => BridgeDebugInstall,
            "cdp-debug" => BridgeReleaseInstall,
            _ => BridgeReleaseInstall
        };
}
