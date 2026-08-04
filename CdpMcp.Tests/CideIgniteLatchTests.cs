using Xunit;

namespace CdpMcp.Tests;

public class CideIgniteLatchTests : IDisposable
{
    readonly string _root;

    public CideIgniteLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ignite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIgniteLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIgniteLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_active_writes_chrome_hint()
    {
        CideIgniteLatch.Publish(
            active: true,
            pulse: "ignite · continuity · armed=1 · next 15:42:27Z",
            armedCount: 1,
            awaitingCount: 0,
            providerBlocked: false);

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideIgniteLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.True(latch.Active);
        Assert.Equal(1, latch.ArmedCount);
        Assert.Equal("ignite · continuity · armed=1 · next 15:42:27Z", latch.ChromeHint);
    }

    [Fact]
    public void Publish_idle_clears_chrome_hint()
    {
        CideIgniteLatch.Publish(active: true, pulse: "ignite · continuity · armed=1", armedCount: 1, awaitingCount: 0, providerBlocked: false);
        CideIgniteLatch.Publish(active: false, pulse: "ignite · continuity · armed=0", armedCount: 0, awaitingCount: 0, providerBlocked: false);

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.Active);
        Assert.Null(latch.ChromeHint);
    }

    [Fact]
    public void Publish_writes_autonomous_hild_course()
    {
        CideIgniteLatch.Publish(
            active: false,
            pulse: "ignite · continuity · armed=0",
            armedCount: 0,
            awaitingCount: 0,
            providerBlocked: false,
            autonomous: true,
            hild: false,
            course: "1. Glass Done (human flight)");

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.True(latch!.Autonomous);
        Assert.False(latch.Hild);
        Assert.Equal("1. Glass Done (human flight)", latch.Course);
        Assert.Null(latch.ChromeHint);
    }

    [Fact]
    public void Publish_writes_await_partner_mode()
    {
        CideIgniteLatch.Publish(
            active: true,
            pulse: "ignite · continuity · awaiting_partner · latch=1",
            armedCount: 0,
            awaitingCount: 1,
            providerBlocked: false,
            autonomous: true,
            awaitPartner: true,
            mode: "talk");

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.True(latch!.AwaitPartner);
        Assert.Equal("talk", latch.Mode);
        Assert.True(latch.Autonomous);
    }

    [Fact]
    public void Publish_halt_mode()
    {
        CideIgniteLatch.Publish(
            active: true,
            pulse: "ignite · halt · await partner",
            armedCount: 0,
            awaitingCount: 1,
            providerBlocked: false,
            autonomous: false,
            awaitPartner: true,
            mode: "halt");

        var latch = CideIgniteLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("halt", latch!.Mode);
        Assert.True(latch.AwaitPartner);
        Assert.False(latch.Autonomous);
    }
}
