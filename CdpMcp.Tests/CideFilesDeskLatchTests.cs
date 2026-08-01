using Xunit;

namespace CdpMcp.Tests;

public class CideFilesDeskLatchTests : IDisposable
{
    readonly string _root;

    public CideFilesDeskLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-files-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideFilesDeskLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideFilesDeskLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideFilesDeskLatch.Publish(
            active: true,
            pulse: "files · project · cascade-ide · 12",
            op: "scene",
            where: "project",
            cwd: @"D:\ws\cascade-ide",
            entryCount: 12);

        var latch = CideFilesDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideFilesDeskLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("scene", latch.Op);
        Assert.Equal("project", latch.Where);
        Assert.Equal(12, latch.EntryCount);
        Assert.Equal("files · project · cascade-ide · 12", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideFilesDeskLatch.Publish(
            active: true,
            pulse: "files · project · cascade-ide · 12",
            op: "scene",
            where: "project",
            cwd: @"D:\ws\cascade-ide",
            entryCount: 12);
        CideFilesDeskLatch.Publish(
            active: false,
            pulse: "files_desk · idle · cwd cleared",
            op: "clear",
            where: null,
            cwd: null,
            entryCount: 0);

        var latch = CideFilesDeskLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
        Assert.Equal("clear", latch.Op);
    }
}
