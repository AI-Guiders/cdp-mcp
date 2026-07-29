using Xunit;

namespace CdpMcp.Tests;

public class CidePressureLatchTests : IDisposable
{
    readonly string _root;

    public CidePressureLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-pressure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CidePressureLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CidePressureLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_armed_writes_chrome_hint()
    {
        CidePressureLatch.Publish(armed: true, pulse: "pressure · ARMED · stashed", hasStash: true);

        var latch = CidePressureLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CidePressureLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Armed);
        Assert.True(latch.HasStash);
        Assert.Equal("pressure · ARMED · stashed", latch.Pulse);
        Assert.Equal("pressure · ARMED · stashed", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CidePressureLatch.Publish(armed: true, pulse: "pressure · ARMED · stashed", hasStash: true);
        CidePressureLatch.Publish(armed: false, pulse: "pressure · idle", hasStash: true);

        var latch = CidePressureLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Armed);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("pressure · idle", latch.Pulse);
    }
}
