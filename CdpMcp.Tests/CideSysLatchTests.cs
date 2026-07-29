using Xunit;

namespace CdpMcp.Tests;

public class CideSysLatchTests : IDisposable
{
    readonly string _root;

    public CideSysLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-sys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideSysLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideSysLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideSysLatch.Publish(
            active: true,
            pulse: "ops · seat=cdp · live=16:01:52Z · staged · armed=1",
            seat: "cdp",
            pending: true);

        var latch = CideSysLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideSysLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.True(latch.Pending);
        Assert.Equal("cdp", latch.Seat);
        Assert.Equal("ops · seat=cdp · live=16:01:52Z · staged · armed=1", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideSysLatch.Publish(active: true, pulse: "ops · seat=cdp · staged · armed=1", seat: "cdp", pending: true);
        CideSysLatch.Publish(active: false, pulse: "ops · seat=cdp · clear · armed=0", seat: "cdp", pending: false);

        var latch = CideSysLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
