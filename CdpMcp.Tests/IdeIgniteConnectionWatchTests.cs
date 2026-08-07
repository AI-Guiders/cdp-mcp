using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteConnectionWatchTests
{
    [Fact]
    public void Start_marks_running_and_Stop_clears()
    {
        try
        {
            IdeIgniteConnectionWatch.Stop();
            Assert.False(IdeIgniteConnectionWatch.IsRunning);

            IdeIgniteConnectionWatch.Start(1); // unreachable port — loop probes, no throw
            Assert.True(IdeIgniteConnectionWatch.IsRunning);
            Assert.Equal(0, IdeIgniteConnectionWatch.ConnectionRetryClickCount);

            IdeIgniteConnectionWatch.Stop();
            Assert.False(IdeIgniteConnectionWatch.IsRunning);
        }
        finally
        {
            IdeIgniteConnectionWatch.Stop();
        }
    }

    [Fact]
    public void Start_restart_resets_click_count()
    {
        try
        {
            IdeIgniteConnectionWatch.Start(1);
            IdeIgniteConnectionWatch.Start(1);
            Assert.True(IdeIgniteConnectionWatch.IsRunning);
            Assert.Equal(0, IdeIgniteConnectionWatch.ConnectionRetryClickCount);
        }
        finally
        {
            IdeIgniteConnectionWatch.Stop();
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
