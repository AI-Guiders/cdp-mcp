using Xunit;

namespace CdpMcp.Tests;

public class CideMcpLatchTests : IDisposable
{
    readonly string _root;

    public CideMcpLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-mcp-latch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideMcpLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideMcpLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideMcpLatch.Publish(active: true, pulse: "mcp · 2 mounted", mounted: 2);

        var latch = CideMcpLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideMcpLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(2, latch.Mounted);
        Assert.Equal("mcp · 2 mounted", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideMcpLatch.Publish(active: true, pulse: "mcp · 1 mounted", mounted: 1);
        CideMcpLatch.Publish(active: false, pulse: "mcp · idle", mounted: 0);

        var latch = CideMcpLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Equal(0, latch.Mounted);
        Assert.Null(latch.ChromeHint);
    }
}
