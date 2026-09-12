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

    [Theory]
    [InlineData("0.5.409+d9ce1329b312", "d9ce1329b312")]
    [InlineData("0.2.0+abc123", "abc123")]
    [InlineData("0.5.373", null)]
    [InlineData("", null)]
    public void CommitSuffix_extracts_build_stamp(string? raw, string? expected)
        => Assert.Equal(expected, IdeOpsPulse.CommitSuffix(raw));

    [Theory]
    [InlineData("CdpMcp.exe", "CdpService.exe", true)]
    [InlineData("CdpService.exe", "CdpMcp.exe", true)]
    [InlineData("CdpMcpBridge.exe", "CdpMcpBridge.exe", true)]
    [InlineData("CdpMcpBridge.exe", "CdpService.exe", false)]
    [InlineData("CdpMcp.exe", "CdpMcpBridge.exe", false)]
    public void VersionFamily_groups_service_and_bridge_exes(string selfExe, string sibExe, bool sameFamily)
        => Assert.Equal(sameFamily, string.Equals(
            IdeOpsPulse.VersionFamily(selfExe),
            IdeOpsPulse.VersionFamily(sibExe),
            StringComparison.Ordinal));

    [Fact]
    public void SeatVersionsLag_false_when_service_and_bridge_share_commit()
    {
        var self = new IdeOpsPulse.InstallProbe("CdpService.exe", "0.5.764", "abc123def456");
        var sib = new IdeOpsPulse.InstallProbe("CdpMcpBridge.exe", "0.2.0", "abc123def456");
        Assert.False(IdeOpsPulse.SeatVersionsLag(self, sib));
    }

    [Fact]
    public void SeatVersionsLag_true_when_commit_suffixes_differ()
    {
        var self = new IdeOpsPulse.InstallProbe("CdpService.exe", "0.5.764", "abc123");
        var sib = new IdeOpsPulse.InstallProbe("CdpMcpBridge.exe", "0.2.0", "def456");
        Assert.True(IdeOpsPulse.SeatVersionsLag(self, sib));
    }

    [Fact]
    public void SeatVersionsLag_false_when_incompatible_schemes_without_commit()
    {
        var self = new IdeOpsPulse.InstallProbe("CdpService.exe", "0.5.764", null);
        var sib = new IdeOpsPulse.InstallProbe("CdpMcpBridge.exe", "0.2.0", null);
        Assert.False(IdeOpsPulse.SeatVersionsLag(self, sib));
    }

    [Fact]
    public void SeatVersionsLag_true_within_same_family_without_commit()
    {
        var self = new IdeOpsPulse.InstallProbe("CdpMcp.exe", "0.5.763", null);
        var sib = new IdeOpsPulse.InstallProbe("CdpService.exe", "0.5.764", null);
        Assert.True(IdeOpsPulse.SeatVersionsLag(self, sib));
    }

    [Fact]
    public void SeatVersionsLag_false_within_same_family_same_short_version()
    {
        var self = new IdeOpsPulse.InstallProbe("CdpMcpBridge.exe", "0.2.0", null);
        var sib = new IdeOpsPulse.InstallProbe("CdpMcpBridge.exe", "0.2.0", null);
        Assert.False(IdeOpsPulse.SeatVersionsLag(self, sib));
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
