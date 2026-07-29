using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class CideAlertLatchTests : IDisposable
{
    readonly string _root;

    public CideAlertLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-alert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideAlertLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideAlertLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_clear_writes_empty_lines()
    {
        var snap = IdeAlertChannel.Build(
            new QualityGates.QualitySnap(true, 0, 0, false, "ok"),
            diskChanged: 0,
            dapActive: false,
            dapStopped: false);

        CideAlertLatch.Publish(snap);

        var latch = CideAlertLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideAlertLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.Equal("clear", latch.Level);
        Assert.Empty(latch.Lines ?? []);
    }

    [Fact]
    public void Publish_warn_keeps_pulse_and_lines()
    {
        var snap = IdeAlertChannel.Build(
            new QualityGates.QualitySnap(true, Warn: 2, Fail: 0, SuggestSniper: false, Pulse: "WARN×2"),
            diskChanged: 0,
            dapActive: false,
            dapStopped: false);

        CideAlertLatch.Publish(snap);

        var latch = CideAlertLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("warn", latch!.Level);
        Assert.False(string.IsNullOrWhiteSpace(latch.Pulse));
        Assert.NotEmpty(latch.Lines ?? []);
    }
}
