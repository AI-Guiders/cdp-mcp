using Xunit;

namespace CdpMcp.Tests;

public class CidePluginsLatchTests : IDisposable
{
    readonly string _root;

    public CidePluginsLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CidePluginsLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CidePluginsLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_mode_a_writes_chrome_hint()
    {
        CidePluginsLatch.Publish(
            active: true,
            pulse: "plugins · 2 attn (1 Mode A) · go=plugins",
            attentionCount: 2,
            modeA: 1,
            hidden: 0);

        var latch = CidePluginsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CidePluginsLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(2, latch.AttentionCount);
        Assert.Equal(1, latch.ModeA);
        Assert.Equal("plugins · 2 attn (1 Mode A) · go=plugins", latch.ChromeHint);
    }

    [Fact]
    public void Publish_healthy_clears_chrome_hint()
    {
        CidePluginsLatch.Publish(
            active: true,
            pulse: "plugins · attention empty (3 off — enable group/plugin)",
            attentionCount: 0,
            modeA: 0,
            hidden: 3);
        CidePluginsLatch.Publish(
            active: false,
            pulse: "plugins · 2 attn (0 Mode A) · go=plugins",
            attentionCount: 2,
            modeA: 0,
            hidden: 1);

        var latch = CidePluginsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(2, latch.AttentionCount);
        Assert.Equal(0, latch.ModeA);
    }
}
