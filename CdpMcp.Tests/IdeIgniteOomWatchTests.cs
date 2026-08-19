using Xunit;

namespace CdpMcp.Tests;

[Collection("IgniteSerial")]
public sealed class IdeIgniteOomWatchTests : IDisposable
{
    readonly string _root;
    readonly string? _prevCdtEdgeEnv;

    public IdeIgniteOomWatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-oom-watch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeRemountWake.RootOverrideForTests = _root;
        _prevCdtEdgeEnv = Environment.GetEnvironmentVariable("CDP_OOM_WAKE_CDT_EDGE");
    }

    public void Dispose()
    {
        IdeRemountWake.RootOverrideForTests = null;
        Environment.SetEnvironmentVariable("CDP_OOM_WAKE_CDT_EDGE", _prevCdtEdgeEnv);
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
        Environment.SetEnvironmentVariable("CDP_OOM_WAKE_CDT_EDGE", null);
        Directory.CreateDirectory(IdeRemountWake.StateRoot);
        File.WriteAllText(IdeRemountWake.PendingPathForSeat("cdp"), "{}");
        Assert.True(IdeRemountWake.HasAnyPending());
        Assert.False(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_false_when_cdt_edge_env_off()
    {
        Environment.SetEnvironmentVariable("CDP_OOM_WAKE_CDT_EDGE", "0");
        Assert.False(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }

    [Fact]
    public void ShouldScheduleCdtEdgeOomWake_true_when_no_remount_and_edge_on()
    {
        Environment.SetEnvironmentVariable("CDP_OOM_WAKE_CDT_EDGE", null);
        Assert.False(IdeRemountWake.HasPending("cdp"));
        Assert.True(IdeIgniteOomWatch.ShouldScheduleCdtEdgeOomWake());
    }
}
