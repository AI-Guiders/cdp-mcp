#nullable enable
using CdpMcpBridge;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpBridgeGatekeeperEnsurerTests
{
    [Fact]
    public void ResolveInstallDir_prefers_configured_over_slot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-gatekeeper-ensurer-" + Guid.NewGuid().ToString("N"));
        var slot = Path.Combine(Path.GetTempPath(), "cdp-slot-ensurer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(dir, "CdpGatekeeper.exe"), "");
        File.WriteAllText(Path.Combine(slot, "CdpGatekeeper.exe"), "");
        try
        {
            Assert.Equal(dir, CdpBridgeGatekeeperEnsurer.ResolveInstallDir(dir, slot));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(slot, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_disabled_when_auto_start_tower_false()
    {
        var settings = new CdpBridgeSettings
        {
            BaseUrl = new Uri("http://127.0.0.1:8771/"),
            Token = "t",
            AutoStartTower = false,
            GatekeeperInstallDir = @"D:\cdp-gatekeeper"
        };

        Assert.Null(CdpBridgeGatekeeperEnsurer.TryCreate(settings));
    }
}
