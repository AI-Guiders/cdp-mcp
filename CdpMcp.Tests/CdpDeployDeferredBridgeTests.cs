using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpDeployDeferredBridgeTests : IDisposable
{
    readonly string _root = Path.Combine(
        Path.GetTempPath(), "cdp-deferred-bridge-tests", Guid.NewGuid().ToString("N"));

    CdpDeployLayout Layout()
    {
        var service = Path.Combine(_root, "service");
        Directory.CreateDirectory(service);
        return new CdpDeployLayout(
            service,
            Path.Combine(_root, "bridge-release"),
            Path.Combine(_root, "bridge-debug"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void WriteDeferredBridge_keeps_bridge_root_and_empties_service_root()
    {
        var layout = Layout();
        Directory.CreateDirectory(layout.StagedBridgeRelease);

        CdpDeployPending.WriteDeferredBridge(layout, layout.StagedBridgeRelease);
        var pending = CdpDeployPending.ReadRequired(layout);

        Assert.Equal(string.Empty, pending.ServiceRoot);
        Assert.Equal(layout.StagedBridgeRelease, pending.BridgeRoot);
        Assert.Contains("bridge deferred", pending.ApplyHint);
    }

    [Fact]
    public void ResolveStagedServiceRoot_falls_back_to_layout_when_service_already_landed()
    {
        var layout = Layout();
        var pending = new CdpDeployPending.PendingUpdate(
            "cdp_pending_update/v0", "soft", DateTime.UtcNow.ToString("o"),
            "", layout.StagedBridgeRelease, null, "");

        // Service already landed: pending empty + no StagedService dir → null (skip promote).
        Assert.Null(CdpDeployOrchestrator.ResolveStagedServiceRoot(pending, layout));

        // Bridge-only apply with a fresh service staged root → uses it.
        Directory.CreateDirectory(layout.StagedService);
        Assert.Equal(layout.StagedService, CdpDeployOrchestrator.ResolveStagedServiceRoot(pending, layout));

        // Normal pending with valid ServiceRoot wins over layout.
        var customRoot = Path.Combine(_root, "pending-root");
        Directory.CreateDirectory(customRoot);
        var normal = pending with { ServiceRoot = customRoot };
        Assert.Equal(customRoot, CdpDeployOrchestrator.ResolveStagedServiceRoot(normal, layout));
    }
}