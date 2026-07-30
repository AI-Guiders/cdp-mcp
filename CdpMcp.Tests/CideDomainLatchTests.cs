using Xunit;

namespace CdpMcp.Tests;

public class CideDomainLatchTests : IDisposable
{
    readonly string _root;

    public CideDomainLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-domain-latch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideDomainLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideDomainLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_cards_writes_chrome_hint()
    {
        CideDomainLatch.Publish(
            active: true,
            pulse: "domain · 4 cards · [tm]",
            cardCount: 4);

        var latch = CideDomainLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideDomainLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(4, latch.CardCount);
        Assert.Equal("domain · 4 cards · [tm]", latch.ChromeHint);
    }

    [Fact]
    public void Publish_empty_clears_chrome_hint()
    {
        CideDomainLatch.Publish(active: true, pulse: "domain · 1 cards · [tm]", cardCount: 1);
        CideDomainLatch.Publish(active: false, pulse: "domain · empty · .cdp/domain", cardCount: 0);

        var latch = CideDomainLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(0, latch.CardCount);
    }
}
