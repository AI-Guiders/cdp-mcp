using Xunit;

namespace CdpMcp.Tests;

public class CideDebugDeskLatchTests : IDisposable
{
    readonly string _root;

    public CideDebugDeskLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-debug-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideDebugDeskLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideDebugDeskLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_stopped_writes_chrome_hint()
    {
        CideDebugDeskLatch.Publish(
            active: true,
            pulse: "debug_desk · continue · STOPPED t=1 · bp=2",
            verdict: "continue",
            bpCount: 2,
            stopped: true,
            activeDap: true);

        var latch = CideDebugDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideDebugDeskLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("continue", latch.Verdict);
        Assert.Equal(2, latch.BpCount);
        Assert.True(latch.Stopped);
        Assert.True(latch.ActiveDap);
        Assert.Equal("debug_desk · continue · STOPPED t=1 · bp=2", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideDebugDeskLatch.Publish(
            active: true,
            pulse: "debug_desk · continue · STOPPED t=1 · bp=2",
            verdict: "continue",
            bpCount: 2,
            stopped: true,
            activeDap: true);
        CideDebugDeskLatch.Publish(
            active: false,
            pulse: "debug_desk · idle · idle · bp=2",
            verdict: "idle",
            bpCount: 2,
            stopped: false,
            activeDap: false);

        var latch = CideDebugDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("idle", latch.Verdict);
        Assert.Equal(2, latch.BpCount);
    }
}
