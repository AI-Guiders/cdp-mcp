using Xunit;

namespace CdpMcp.Tests;

public class CideToolchainLatchTests : IDisposable
{
    readonly string _root;

    public CideToolchainLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-toolchain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideToolchainLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideToolchainLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_missing_writes_chrome_hint()
    {
        CideToolchainLatch.Publish(
            active: true,
            pulse: "toolchain · 3/5 ok · go=toolchain",
            okCount: 3,
            totalCount: 5);

        var latch = CideToolchainLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideToolchainLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(3, latch.OkCount);
        Assert.Equal(5, latch.TotalCount);
        Assert.Equal("toolchain · 3/5 ok · go=toolchain", latch.ChromeHint);
    }

    [Fact]
    public void Publish_all_ok_clears_chrome_hint()
    {
        CideToolchainLatch.Publish(active: true, pulse: "toolchain · 3/5 ok · go=toolchain", okCount: 3, totalCount: 5);
        CideToolchainLatch.Publish(active: false, pulse: "toolchain · 5/5 ok · go=toolchain", okCount: 5, totalCount: 5);

        var latch = CideToolchainLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal(5, latch.OkCount);
        Assert.Equal(5, latch.TotalCount);
    }
}
