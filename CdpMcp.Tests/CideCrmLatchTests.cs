using Xunit;

namespace CdpMcp.Tests;

public class CideCrmLatchTests : IDisposable
{
    readonly string _root;

    public CideCrmLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-crm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideCrmLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideCrmLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_awaiting_writes_chrome_hint()
    {
        CideCrmLatch.Publish(
            active: true,
            pulse: "crm · AWAITING · plan:stage-1",
            status: "awaiting",
            kind: "plan",
            refId: "stage-1");

        var latch = CideCrmLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideCrmLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("awaiting", latch.Status);
        Assert.Equal("plan", latch.Kind);
        Assert.Equal("stage-1", latch.RefId);
        Assert.Equal("crm · AWAITING · plan:stage-1", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideCrmLatch.Publish(active: true, pulse: "crm · AWAITING · x", status: "awaiting", kind: "x", refId: "1");
        CideCrmLatch.Publish(active: false, pulse: "crm · idle", status: null, kind: null, refId: null);

        var latch = CideCrmLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Null(latch.Status);
    }
}
