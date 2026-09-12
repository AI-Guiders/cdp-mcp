using Cdp.Config;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IgniteSerial")]
public sealed class IdeIgniteOomWatchTests : IDisposable
{
    readonly string _root;
    readonly CdpOpsConfig _prevOps;

    public IdeIgniteOomWatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-oom-watch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeRemountWake.RootOverrideForTests = _root;
        _prevOps = CdpOpsConfig.Current;
    }

    public void Dispose()
    {
        IdeRemountWake.RootOverrideForTests = null;
        CdpOpsConfig.RestoreForTests(_prevOps);
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* best-effort */ }
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_false_when_remount_pending()
    {
        CdpOpsConfig.Bind(new CdpConfigDocument { Ops = new CdpConfigOpsSection { OomWakeCdtEdge = true } });
        Directory.CreateDirectory(IdeRemountWake.StateRoot);
        File.WriteAllText(IdeRemountWake.PendingPathForSeat("cdp"), "{}");
        Assert.True(IdeRemountWake.HasAnyPending());
        Assert.False(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_false_by_default()
    {
        CdpOpsConfig.Bind(new CdpConfigDocument());
        Assert.False(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_false_when_cdt_edge_disabled()
    {
        CdpOpsConfig.Bind(new CdpConfigDocument { Ops = new CdpConfigOpsSection { OomWakeCdtEdge = false } });
        Assert.False(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_true_when_edge_opt_in_and_no_remount()
    {
        CdpOpsConfig.Bind(new CdpConfigDocument { Ops = new CdpConfigOpsSection { OomWakeCdtEdge = true } });
        Assert.False(IdeRemountWake.HasPending("cdp"));
        Assert.True(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake(remountArmed: false));
    }
}
