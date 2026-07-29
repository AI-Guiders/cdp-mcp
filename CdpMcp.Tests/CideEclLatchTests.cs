using Xunit;

namespace CdpMcp.Tests;

public class CideEclLatchTests : IDisposable
{
    readonly string _root;

    public CideEclLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-ecl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideEclLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideEclLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    static IdeChkChannel.ProbeCtx Ctx() =>
        new(
            ProjectOpen: true,
            TaskOpen: true,
            IgniteIdle: true,
            GitKnown: true,
            GitDirty: false,
            TestsGreen: false,
            TestsFailed: false,
            ProblemsClean: true,
            DapStopped: false,
            DapActive: false,
            SniperOk: true,
            Phase: "explore",
            Intent: null);

    [Fact]
    public void Publish_writes_hot_when_active()
    {
        var snap = IdeChkChannel.Build(Ctx());

        CideEclLatch.Publish(snap);

        var latch = CideEclLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideEclLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.False(string.IsNullOrWhiteSpace(latch.Pulse));
        Assert.False(string.IsNullOrWhiteSpace(latch.HotId));
        Assert.True(latch.OpenRequired >= 0);
    }
}
