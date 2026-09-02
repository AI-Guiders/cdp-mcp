#nullable enable

using AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness.DataAcquisition;

namespace CdpMcp.Cockpit.DataAcquisition;

/// <summary>DAL locus: resolve toolchain binaries on PATH (delegates to platform W4).</summary>
public static class ToolchainPathProbe
{
    public static string? Resolve(string bin) => global::AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness.DataAcquisition.ToolchainPathProbe.Resolve(bin);

    public static void RefreshProcessPath() =>
        global::AIGuiders.Platform.Execution.Cockpit.Channels.EnvironmentReadiness.DataAcquisition.ToolchainPathProbe.RefreshProcessPath();
}
