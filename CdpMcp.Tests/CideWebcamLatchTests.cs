using Xunit;

namespace CdpMcp.Tests;

public class CideWebcamLatchTests : IDisposable
{
    readonly string _root;

    public CideWebcamLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-webcam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideWebcamLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideWebcamLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_capture_writes_chrome_hint()
    {
        CideWebcamLatch.Publish(
            active: true,
            pulse: "webcam · frame · 1280x720",
            op: "frame",
            path: "C:\\tmp\\snap.jpg");

        var latch = CideWebcamLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideWebcamLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("frame", latch.Op);
        Assert.Equal("C:\\tmp\\snap.jpg", latch.Path);
        Assert.Equal("webcam · frame · 1280x720", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideWebcamLatch.Publish(active: true, pulse: "webcam · frame · 1x1", op: "frame", path: "a.jpg");
        CideWebcamLatch.Publish(active: false, pulse: "webcam · idle", op: null, path: null);

        var latch = CideWebcamLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Null(latch.Op);
    }
}
