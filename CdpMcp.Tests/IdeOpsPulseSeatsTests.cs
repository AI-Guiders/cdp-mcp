using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeOpsPulseSeatsTests
{
    [Theory]
    [InlineData("0.5.409+d9ce1329b312", "0.5.409")]
    [InlineData("0.5.373", "0.5.373")]
    [InlineData("1.2.3 metadata", "1.2.3")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ShortVersion_strips_commit_and_space(string? raw, string? expected)
        => Assert.Equal(expected, IdeOpsPulse.ShortVersion(raw));

    [Fact]
    public void SiblingRootForSeat_pairs_release_and_debug()
    {
        Assert.Equal(IdeDeploy.DebugTarget, IdeOpsPulse.SiblingRootForSeat("cdp"));
        Assert.Equal(IdeDeploy.ReleaseTarget, IdeOpsPulse.SiblingRootForSeat("cdp-debug"));
        Assert.Equal(IdeDeploy.ReleaseTarget, IdeOpsPulse.SiblingRootForSeat("other"));
    }

    [Fact]
    public void TryInstallProductVersion_reads_bridge_exe_when_monolith_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-ops-pulse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var self = Environment.ProcessPath
                       ?? throw new InvalidOperationException("ProcessPath unavailable.");
            File.Copy(self, Path.Combine(root, "CdpMcpBridge.exe"));

            var version = IdeOpsPulse.TryInstallProductVersion(root);
            Assert.NotNull(version);
            Assert.DoesNotContain('+', version);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
