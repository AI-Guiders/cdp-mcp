using Xunit;

namespace CdpMcp.Tests;

public class CideArchLatchTests : IDisposable
{
    readonly string _root;

    public CideArchLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-arch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideArchLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideArchLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideArchLatch.Publish(
            active: true,
            pulse: "as_built · cide · 12 roles · open=3 elect=1 promo=0 · edges=4",
            profile: "cide",
            mode: "as_built");

        var latch = CideArchLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideArchLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("cide", latch.Profile);
        Assert.Equal("as_built", latch.Mode);
        Assert.Equal("as_built · cide · 12 roles · open=3 elect=1 promo=0 · edges=4", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideArchLatch.Publish(active: true, pulse: "arch_board · 2 roles", profile: null, mode: "board");
        CideArchLatch.Publish(active: false, pulse: "arch_board · empty", profile: null, mode: null);

        var latch = CideArchLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
