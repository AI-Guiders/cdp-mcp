#nullable enable
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class ListToolsAmortiseTests : IDisposable
{
    public ListToolsAmortiseTests()
    {
        VisibleToolsSessionCache.ResetForTests();
    }

    public void Dispose() => VisibleToolsSessionCache.ResetForTests();

    [Fact]
    public void MetaToolCatalog_All_is_stable_singleton()
    {
        var a = MetaToolCatalog.All;
        var b = MetaToolCatalog.All;
        Assert.Same(a, b);
        Assert.True(a.Count > 10);
        Assert.Contains(a, t => t.Name == "cdp_ignite");
    }

    [Fact]
    public void SoftInstrument_hides_ignite_and_pressure()
    {
        Assert.Contains("cdp_ignite", VisibleToolCatalog.SoftInstrumentMetaNames);
        Assert.Contains("cdp_pressure", VisibleToolCatalog.SoftInstrumentMetaNames);
    }

    [Fact]
    public void VisibleToolsSessionCache_hit_on_same_fingerprint()
    {
        var session = new SessionContext();
        var fp = VisibleToolsSessionCache.Fingerprint(7, session);
        var tools = new List<ModelContextProtocol.Protocol.Tool>
        {
            new() { Name = "cdp_health", Description = "x" }
        };
        VisibleToolsSessionCache.Put(fp, tools);
        Assert.True(VisibleToolsSessionCache.TryGet(fp, out var hit));
        Assert.NotNull(hit);
        Assert.Single(hit!);
        Assert.Equal("cdp_health", hit[0].Name);
        var stats = VisibleToolsSessionCache.Stats();
        Assert.True(stats.Hits >= 1);
        Assert.True(stats.Warm);
    }

    [Fact]
    public void VisibleToolsSessionCache_invalidate_clears()
    {
        var session = new SessionContext();
        var fp = VisibleToolsSessionCache.Fingerprint(1, session);
        VisibleToolsSessionCache.Put(fp, [new ModelContextProtocol.Protocol.Tool { Name = "a" }]);
        VisibleToolsSessionCache.Invalidate();
        Assert.False(VisibleToolsSessionCache.TryGet(fp, out _));
    }
}
