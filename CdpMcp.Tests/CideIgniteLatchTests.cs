using Xunit;

namespace CdpMcp.Tests;

public class CideIgniteLatchTests : IDisposable
{
    readonly string _root;

    public CideIgniteLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ignite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIgniteLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIgniteLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideIgniteLatch.Publish(
            active: true,
            pulse: "ignite · continuity · armed=1 · next 15:42:27Z",
            armedCount: 1,
            awaitingCount: 0,
            providerBlocked: false);

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideIgniteLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(1, latch.ArmedCount);
        Assert.Equal("ignite · continuity · armed=1 · next 15:42:27Z", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideIgniteLatch.Publish(active: true, pulse: "ignite · continuity · armed=1", armedCount: 1, awaitingCount: 0, providerBlocked: false);
        CideIgniteLatch.Publish(active: false, pulse: "ignite · continuity · armed=0", armedCount: 0, awaitingCount: 0, providerBlocked: false);

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
