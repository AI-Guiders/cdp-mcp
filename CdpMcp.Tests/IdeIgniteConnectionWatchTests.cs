using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteConnectionWatchTests
{
    [Fact]
    public void StartConnectionWatch_marks_running_and_Stop_clears()
    {
        try
        {
            IdeIgniteArmHost.StopConnectionWatch();
            Assert.False(IdeIgniteArmHost.IsConnectionWatchRunning);

            IdeIgniteArmHost.StartConnectionWatch(1); // unreachable port — loop probes, no throw
            Assert.True(IdeIgniteArmHost.IsConnectionWatchRunning);
            Assert.Equal(0, IdeIgniteArmHost.ConnectionRetryClickCount);

            IdeIgniteArmHost.StopConnectionWatch();
            Assert.False(IdeIgniteArmHost.IsConnectionWatchRunning);
        }
        finally
        {
            IdeIgniteArmHost.StopConnectionWatch();
        }
    }

    [Fact]
    public void StartConnectionWatch_restart_resets_click_count()
    {
        try
        {
            IdeIgniteArmHost.StartConnectionWatch(1);
            IdeIgniteArmHost.StartConnectionWatch(1);
            Assert.True(IdeIgniteArmHost.IsConnectionWatchRunning);
            Assert.Equal(0, IdeIgniteArmHost.ConnectionRetryClickCount);
        }
        finally
        {
            IdeIgniteArmHost.StopConnectionWatch();
        }
    }

    [Fact]
    public async Task TryDismissConnectionProblemsOnPortAsync_unreachable_returns_false()
    {
        var ok = await IdeIgniteChannel.TryDismissConnectionProblemsOnPortAsync(
            port: 1,
            CancellationToken.None);
        Assert.False(ok);
    }
}
