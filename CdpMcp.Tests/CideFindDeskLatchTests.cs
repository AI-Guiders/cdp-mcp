using Xunit;

namespace CdpMcp.Tests;

public class CideFindDeskLatchTests : IDisposable
{
    readonly string _root;

    public CideFindDeskLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-find-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideFindDeskLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideFindDeskLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideFindDeskLatch.Publish(
            active: true,
            pulse: "find · project · 3 hit(s)",
            op: "run",
            where: "project",
            query: "SoftOrgan",
            hitCount: 3);

        var latch = CideFindDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideFindDeskLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("run", latch.Op);
        Assert.Equal("project", latch.Where);
        Assert.Equal("SoftOrgan", latch.Query);
        Assert.Equal(3, latch.HitCount);
        Assert.Equal("find · project · 3 hit(s)", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideFindDeskLatch.Publish(
            active: true,
            pulse: "find · project · 3 hit(s)",
            op: "run",
            where: "project",
            query: "SoftOrgan",
            hitCount: 3);
        CideFindDeskLatch.Publish(
            active: false,
            pulse: "find_desk · idle · cleared",
            op: "clear",
            where: null,
            query: null,
            hitCount: 0);

        var latch = CideFindDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("clear", latch.Op);
    }
}
