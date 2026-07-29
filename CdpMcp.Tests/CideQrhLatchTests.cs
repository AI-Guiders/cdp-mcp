using Xunit;

namespace CdpMcp.Tests;

public class CideQrhLatchTests : IDisposable
{
    readonly string _root;

    public CideQrhLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-qrh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideQrhLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideQrhLatch.RootOverrideForTests = null;
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
    public void Publish_writes_hot_and_related()
    {
        var snap = IdeQrhChannel.Build(Ctx());

        CideQrhLatch.Publish(snap);

        var latch = CideQrhLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(CideQrhLatch.Schema, latch!.Schema);
        Assert.Equal("agent", latch.Origin);
        Assert.False(string.IsNullOrWhiteSpace(latch.Pulse));
        Assert.False(string.IsNullOrWhiteSpace(latch.HotId));
        Assert.False(string.IsNullOrWhiteSpace(latch.HotTitle));
    }
}
