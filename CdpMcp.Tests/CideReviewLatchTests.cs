using Xunit;

namespace CdpMcp.Tests;

public class CideReviewLatchTests : IDisposable
{
    readonly string _root;

    public CideReviewLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideReviewLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideReviewLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_dirty_files_writes_chrome_hint()
    {
        CideReviewLatch.Publish(
            active: true,
            pulse: "review · ready ×3 · go=review",
            fileCount: 3,
            highRisk: 1,
            machineOk: true);

        var latch = CideReviewLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideReviewLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(3, latch.FileCount);
        Assert.Equal(1, latch.HighRisk);
        Assert.True(latch.MachineOk);
        Assert.Equal("review · ready ×3 · go=review", latch.ChromeHint);
    }

    [Fact]
    public void Publish_clean_machine_ok_clears_chrome_hint()
    {
        CideReviewLatch.Publish(active: true, pulse: "review · ready ×1 · go=review", fileCount: 1, highRisk: 0, machineOk: true);
        CideReviewLatch.Publish(active: false, pulse: "review · idle · go=review", fileCount: 0, highRisk: 0, machineOk: true);

        var latch = CideReviewLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(0, latch.FileCount);
    }
}
