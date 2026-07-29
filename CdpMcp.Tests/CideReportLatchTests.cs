using Xunit;

namespace CdpMcp.Tests;

public class CideReportLatchTests : IDisposable
{
    readonly string _root;

    public CideReportLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideReportLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideReportLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideReportLatch.Publish(
            active: true,
            pulse: "report · check ok · scratch.csx",
            path: "scratch.csx",
            mode: "check",
            ok: true);

        var latch = CideReportLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideReportLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("scratch.csx", latch.Path);
        Assert.Equal("check", latch.Mode);
        Assert.True(latch.Ok);
        Assert.Equal("report · check ok · scratch.csx", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideReportLatch.Publish(active: true, pulse: "report · run", path: "a.csx", mode: "run", ok: false);
        CideReportLatch.Publish(active: false, pulse: "report · idle", path: null, mode: null, ok: null);

        var latch = CideReportLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Null(latch.Path);
    }
}
