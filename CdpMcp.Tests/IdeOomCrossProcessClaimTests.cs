using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeOomCrossProcessClaimTests : IDisposable
{
    readonly string _root;

    public IdeOomCrossProcessClaimTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-mcp-tests", "oom-claim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeOomCrossProcessClaim.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IdeOomCrossProcessClaim.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public void TryClaimSchedule_first_wins_second_blocked_within_cooldown()
    {
        Assert.True(IdeOomCrossProcessClaim.TryClaimSchedule(TimeSpan.FromSeconds(60)));
        Assert.False(IdeOomCrossProcessClaim.TryClaimSchedule(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void TryClaimSchedule_allows_after_cooldown_elapsed()
    {
        Assert.True(IdeOomCrossProcessClaim.TryClaimSchedule(TimeSpan.FromMilliseconds(1)));
        Thread.Sleep(30);
        Assert.True(IdeOomCrossProcessClaim.TryClaimSchedule(TimeSpan.FromMilliseconds(1)));
    }
}
