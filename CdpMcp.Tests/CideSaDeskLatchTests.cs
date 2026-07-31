using Xunit;

namespace CdpMcp.Tests;

public class CideSaDeskLatchTests : IDisposable
{
    readonly string _root;

    public CideSaDeskLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-sa-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideSaDeskLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideSaDeskLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideSaDeskLatch.Publish(
            active: true,
            pulse: "sa_desk · touch · 2w/0f",
            verdict: "touch");

        var latch = CideSaDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideSaDeskLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("touch", latch.Verdict);
        Assert.Equal("sa_desk · touch · 2w/0f", latch.ChromeHint);
        Assert.EndsWith("sa-desk-LATEST.json", CideSaDeskLatch.LatchPath);
    }

    [Fact]
    public void Publish_inactive_clears_chrome_hint()
    {
        CideSaDeskLatch.Publish(active: true, pulse: "sa_desk · split · 0w/1f", verdict: "split");
        CideSaDeskLatch.Publish(active: false, pulse: "sa_desk · leave · 0w/0f", verdict: "leave");

        var latch = CideSaDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("leave", latch.Verdict);
    }
}
