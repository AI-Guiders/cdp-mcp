using Xunit;

namespace CdpMcp.Tests;

public class CideOnboardLatchTests : IDisposable
{
    readonly string _root;

    public CideOnboardLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-onboard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideOnboardLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideOnboardLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideOnboardLatch.Publish(
            active: true,
            pulse: "onboard · cascade-ide · cide · entry=12 · vert=8 · docs=yes",
            project: "cascade-ide",
            profileHint: "cide");

        var latch = CideOnboardLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideOnboardLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("cascade-ide", latch.Project);
        Assert.Equal("cide", latch.ProfileHint);
        Assert.Equal("onboard · cascade-ide · cide · entry=12 · vert=8 · docs=yes", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideOnboardLatch.Publish(
            active: true,
            pulse: "onboard · cascade-ide · cide · entry=12 · vert=8 · docs=yes",
            project: "cascade-ide",
            profileHint: "cide");
        CideOnboardLatch.Publish(
            active: false,
            pulse: "onboard · empty",
            project: null,
            profileHint: null);

        var latch = CideOnboardLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
