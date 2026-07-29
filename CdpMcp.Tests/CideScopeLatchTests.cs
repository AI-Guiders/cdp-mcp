using Xunit;

namespace CdpMcp.Tests;

public class CideScopeLatchTests : IDisposable
{
    readonly string _root;

    public CideScopeLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideScopeLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideScopeLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideScopeLatch.Publish(
            active: true,
            pulse: "ps · PRIMARY=cdp-mcp · SCOPE=door-to-singularity",
            primary: "cdp-mcp",
            scope: "door-to-singularity");

        var latch = CideScopeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideScopeLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal("cdp-mcp", latch.Primary);
        Assert.Equal("door-to-singularity", latch.Scope);
        Assert.Equal("ps · PRIMARY=cdp-mcp · SCOPE=door-to-singularity", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideScopeLatch.Publish(active: true, pulse: "ps · PRIMARY=cdp-mcp · SCOPE=x", primary: "cdp-mcp", scope: "x");
        CideScopeLatch.Publish(active: false, pulse: "ps · idle · go=project_switch", primary: null, scope: null);

        var latch = CideScopeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }
}
