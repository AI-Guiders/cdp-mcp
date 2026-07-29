using Xunit;

namespace CdpMcp.Tests;

public class CideRefactorLatchTests : IDisposable
{
    readonly string _root;

    public CideRefactorLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-refactor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideRefactorLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideRefactorLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_hotspots_writes_chrome_hint()
    {
        CideRefactorLatch.Publish(
            active: true,
            pulse: "refactor · hotspots=2 · Foo.cs:file_lines=900 · go=refactor",
            hotspotCount: 2);

        var latch = CideRefactorLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideRefactorLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(2, latch.HotspotCount);
        Assert.Equal("refactor · hotspots=2 · Foo.cs:file_lines=900 · go=refactor", latch.ChromeHint);
    }

    [Fact]
    public void Publish_no_hotspots_clears_chrome_hint()
    {
        CideRefactorLatch.Publish(active: true, pulse: "refactor · hotspots=1 · go=refactor", hotspotCount: 1);
        CideRefactorLatch.Publish(active: false, pulse: "refactor · idle · go=refactor", hotspotCount: 0);

        var latch = CideRefactorLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(0, latch.HotspotCount);
    }
}
