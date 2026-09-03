using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpDeploySeatConfigTests
{
    [Fact]
    public void ResolveSeatConfig_prefers_root_over_config_subdir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-seat-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        try
        {
            File.WriteAllText(Path.Combine(dir, CdpDeploySeatConfig.FileName), "root=1");
            File.WriteAllText(CdpDeploySeatConfig.DevTemplatePath(dir), "nested=1");

            Assert.Equal(Path.Combine(dir, CdpDeploySeatConfig.FileName), CdpDeploySeatConfig.ResolveSeatConfigPath(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void NormalizeInstallSeat_migrates_legacy_config_subdir_to_root()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-seat-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        try
        {
            var nested = CdpDeploySeatConfig.DevTemplatePath(dir);
            File.WriteAllText(nested, "operator=true");

            CdpDeploySeatConfig.NormalizeInstallSeat(dir);

            var root = CdpDeploySeatConfig.SeatConfigPath(dir);
            Assert.True(File.Exists(root));
            Assert.Contains("operator=true", File.ReadAllText(root));
            Assert.False(File.Exists(nested));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PromoteTree_preserves_root_operator_config_over_staged_template()
    {
        var live = Path.Combine(Path.GetTempPath(), "cdp-promote-live-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(Path.GetTempPath(), "cdp-promote-staged-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(live);
            Directory.CreateDirectory(Path.Combine(staged, "config"));
            File.WriteAllText(CdpDeploySeatConfig.SeatConfigPath(live), "operator=live");
            File.WriteAllText(CdpDeploySeatConfig.DevTemplatePath(staged), "template=repo");
            File.WriteAllText(Path.Combine(staged, "marker.txt"), "build");

            CdpDeployPromoter.PromoteTree(staged, live);

            Assert.Equal("operator=live", File.ReadAllText(CdpDeploySeatConfig.SeatConfigPath(live)));
            Assert.False(File.Exists(CdpDeploySeatConfig.DevTemplatePath(live)));
            Assert.True(File.Exists(Path.Combine(live, "marker.txt")));
        }
        finally
        {
            if (Directory.Exists(live))
                Directory.Delete(live, true);
            if (Directory.Exists(staged))
                Directory.Delete(staged, true);
        }
    }
}
