#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CideHandsLatchTests : IDisposable
{
    readonly string _root;

    public CideHandsLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-hands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideHandsLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideHandsLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void PublishRunning_writes_caution_chrome_hint()
    {
        CideHandsLatch.PublishRunning(TimeSpan.FromSeconds(3));

        var latch = CideHandsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideHandsLatch.Schema, latch!.Schema);
        Assert.True(latch.Active);
        Assert.Equal("running", latch.Phase, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CAUTION · RUNNING", latch.ChromeHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3s", latch.ChromeHint, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishDone_writes_ok_fail_items()
    {
        CideHandsLatch.PublishDone(
        [
            new CitizenRouteHost.Applied(
                Raw: "@intent kb",
                Verb: "Kb",
                Ok: true,
                Pulse: "kb · read"),
            new CitizenRouteHost.Applied(
                Raw: "@intent go=plan",
                Verb: "Go",
                Ok: false,
                Go: "plan",
                Reason: "busy")
        ],
        elapsed: TimeSpan.FromSeconds(12));

        var latch = CideHandsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.True(latch!.Active);
        Assert.Equal(1, latch.OkCount);
        Assert.Equal(1, latch.FailCount);
        Assert.Contains("FAIL", latch.ChromeHint, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(latch.Items);
        Assert.Equal(2, latch.Items!.Count);
    }

    [Fact]
    public void Clear_drops_chrome_hint()
    {
        CideHandsLatch.PublishRunning();
        CideHandsLatch.Clear();

        var latch = CideHandsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
