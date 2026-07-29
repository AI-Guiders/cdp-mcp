using Xunit;

namespace CdpMcp.Tests;

public class CidePlanLatchTests : IDisposable
{
    readonly string _root;

    public CidePlanLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CidePlanLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CidePlanLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CidePlanLatch.Publish(
            active: true,
            pulse: "Glass › Wire plan @act · explore",
            feature: "Glass as context economy (0-sync)",
            task: "Wire plan Task Manager pulse");

        var latch = CidePlanLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CidePlanLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("Glass as context economy (0-sync)", latch.Feature);
        Assert.Equal("Wire plan Task Manager pulse", latch.Task);
        Assert.Equal("Glass › Wire plan @act · explore", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CidePlanLatch.Publish(active: true, pulse: "Feat › Task · explore", feature: "Feat", task: "Task");
        CidePlanLatch.Publish(active: false, pulse: "no plan — feature <name> · explore", feature: null, task: null);

        var latch = CidePlanLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Null(latch.Feature);
    }
}
