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
        Assert.Contains("cdp_peel", VisibleToolCatalog.SoftInstrumentMetaNames);
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

    [Fact]
    public void VisibleToolsSessionCache_retains_multiple_phase_fingerprints()
    {
        var sCsharp = new SessionContext { Language = "csharp" };
        var sPython = new SessionContext { Language = "python" };
        var fpCsharp = VisibleToolsSessionCache.Fingerprint(1, sCsharp);
        var fpPython = VisibleToolsSessionCache.Fingerprint(1, sPython);

        VisibleToolsSessionCache.Put(fpCsharp, [new ModelContextProtocol.Protocol.Tool { Name = "recall_tool" }]);
        VisibleToolsSessionCache.Put(fpPython, [new ModelContextProtocol.Protocol.Tool { Name = "act_tool" }]);

        Assert.True(VisibleToolsSessionCache.TryGet(fpCsharp, out var hitRecall));
        Assert.True(VisibleToolsSessionCache.TryGet(fpPython, out var hitAct));
        Assert.Equal("recall_tool", hitRecall![0].Name);
        Assert.Equal("act_tool", hitAct![0].Name);
    }

    [Fact]
    public void CdpBridgeToolsCache_last_good_survives_invalidate()
    {
        CdpMcpBridge.CdpBridgeToolsCache.ResetForTests();
        CdpMcpBridge.CdpBridgeToolsCache.Put(10, [new ModelContextProtocol.Protocol.Tool { Name = "cdp_peek" }]);
        CdpMcpBridge.CdpBridgeToolsCache.Invalidate(11);

        Assert.False(CdpMcpBridge.CdpBridgeToolsCache.TryGet(11, out _));
        Assert.True(CdpMcpBridge.CdpBridgeToolsCache.TryGetLastGood(out var fallback));
        Assert.Single(fallback!);
        Assert.Equal("cdp_peek", fallback![0].Name);
    }

    [Fact]
    public void CdpSlotRegistry_in_memory_upsert_touch_and_remove()
    {
        CdpSlotRegistry.ResetForTests();
        var pid = Environment.ProcessId;
        Assert.True(CdpSlotRegistry.Upsert(new CdpSlotRecord
        {
            Pid = pid,
            Port = 8772,
            Sha = "abc1234",
            BuildUtc = "2026-11-06T00:00:00Z"
        }));

        var fresh = CdpSlotRegistry.Fresh();
        Assert.Single(fresh);
        Assert.Equal(8772, fresh[0].Port);

        Assert.True(CdpSlotRegistry.Touch(pid));
        Assert.True(CdpSlotRegistry.Remove(pid));
        Assert.Empty(CdpSlotRegistry.Fresh());
    }
}
