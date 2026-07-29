using Xunit;

namespace CdpMcp.Tests;

public class CideLearnLatchTests : IDisposable
{
    readonly string _root;

    public CideLearnLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-learn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideLearnLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideLearnLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_cards_writes_chrome_hint()
    {
        CideLearnLatch.Publish(
            active: true,
            pulse: "learn · 3 card(s) · go=learn",
            cardCount: 3);

        var latch = CideLearnLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideLearnLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(3, latch.CardCount);
        Assert.Equal("learn · 3 card(s) · go=learn", latch.ChromeHint);
    }

    [Fact]
    public void Publish_empty_clears_chrome_hint()
    {
        CideLearnLatch.Publish(active: true, pulse: "learn · 1 card(s) · go=learn", cardCount: 1);
        CideLearnLatch.Publish(active: false, pulse: "learn · empty · go=learn op=stash", cardCount: 0);

        var latch = CideLearnLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(0, latch.CardCount);
    }
}
