#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeSeatProcessReclaimTests
{
    [Fact]
    public void PathsEqual_normalizes_case_and_full_path()
    {
        var a = Path.Combine(Path.GetTempPath(), "cdp-seat", "CdpMcp.exe");
        var b = a.ToUpperInvariant();
        Assert.True(IdeSeatProcessReclaim.PathsEqual(a, b));
    }

    [Fact]
    public void PathsEqual_rejects_sibling_install()
    {
        Assert.False(IdeSeatProcessReclaim.PathsEqual(
            @"D:\cdp-mcp\CdpMcp.exe",
            @"D:\cdp-mcp-debug\CdpMcp.exe"));
    }

    [Fact]
    public void CollectOtherSameExePids_excludes_self()
    {
        var self = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(self));
        var pid = Environment.ProcessId;
        var others = IdeSeatProcessReclaim.CollectOtherSameExePids(self!, pid);
        Assert.DoesNotContain(pid, others);
    }

    [Fact]
    public void IsSkipEnabled_reads_env()
    {
        var prev = Environment.GetEnvironmentVariable(IdeSeatProcessReclaim.SkipEnv);
        try
        {
            Environment.SetEnvironmentVariable(IdeSeatProcessReclaim.SkipEnv, "1");
            Assert.True(IdeSeatProcessReclaim.IsSkipEnabled());
            Environment.SetEnvironmentVariable(IdeSeatProcessReclaim.SkipEnv, null);
            Assert.False(IdeSeatProcessReclaim.IsSkipEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdeSeatProcessReclaim.SkipEnv, prev);
        }
    }
}
