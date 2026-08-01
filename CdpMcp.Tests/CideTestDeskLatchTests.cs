using Xunit;

namespace CdpMcp.Tests;

public class CideTestDeskLatchTests : IDisposable
{
    readonly string _root;

    public CideTestDeskLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-test-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideTestDeskLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideTestDeskLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_retest_writes_chrome_hint()
    {
        CideTestDeskLatch.Publish(
            active: true,
            pulse: "test_desk · retest · FAIL 2/5",
            verdict: "retest",
            okCount: 2,
            totalCount: 5,
            failed: 3,
            skipped: 0);

        var latch = CideTestDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideTestDeskLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("retest", latch.Verdict);
        Assert.Equal(2, latch.OkCount);
        Assert.Equal(5, latch.TotalCount);
        Assert.Equal(3, latch.Failed);
        Assert.Equal("test_desk · retest · FAIL 2/5", latch.ChromeHint);
    }

    [Fact]
    public void Publish_green_clears_chrome_hint()
    {
        CideTestDeskLatch.Publish(
            active: true,
            pulse: "test_desk · retest · FAIL 2/5",
            verdict: "retest",
            okCount: 2,
            totalCount: 5,
            failed: 3,
            skipped: 0);
        CideTestDeskLatch.Publish(
            active: false,
            pulse: "test_desk · green · ok 5/5",
            verdict: "green",
            okCount: 5,
            totalCount: 5,
            failed: 0,
            skipped: 0);

        var latch = CideTestDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("green", latch.Verdict);
        Assert.Equal(5, latch.OkCount);
    }
}
