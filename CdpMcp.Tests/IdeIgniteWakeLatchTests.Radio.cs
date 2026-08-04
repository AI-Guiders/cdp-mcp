using Xunit;

namespace CdpMcp.Tests;

public partial class IdeIgniteWakeLatchTests
{
    [Fact]
    public void FormatHabitatIntercomRadio_keeps_short_charge()
    {
        var arm = new IdeIgniteArmHost.IgniteArm { Id = "arm-short", Event = "timer" };
        var body = IdeIgniteArmHost.FormatHabitatIntercomRadio(arm, "reason=escalate busy PF");
        Assert.Equal("reason=escalate busy PF", body);
    }

    [Fact]
    public void FormatHabitatIntercomRadio_collapses_composer_charge_wall()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.LeafWakeArmId,
            Event = "timer",
            Task = "Dig Intercom Radio residual gap 3.2"
        };
        var charge =
            "Resume the current authorized local development task from Task Manager. Habitat=CDP.\n" +
            "## operator_priority (SEALED)\n" +
            "Human-face axe (before act on Glass/#CIDE surfaces)\n" +
            "If you feel completely lost / thread amnesia: compaction likely happened.\n" +
            new string('x', 300);
        var body = IdeIgniteArmHost.FormatHabitatIntercomRadio(arm, charge);
        Assert.Contains("Autoi · leaf", body, StringComparison.Ordinal);
        Assert.Contains("→ PFD.NEXT", body, StringComparison.Ordinal);
        Assert.Contains("delta → Plan · Dig Intercom Radio residual gap 3.2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("operator_priority", body, StringComparison.Ordinal);
        Assert.True(body.Length < 200);
    }

    [Fact]
    public void MirrorTimerWakeToIntercom_skips_idle_pf_on_non_primary_seat()
    {
        IdeIgniteArmHost.BindPrimaryAutoiSeat(false);
        try
        {
            Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
            var arm = new IdeIgniteArmHost.IgniteArm
            {
                Id = "arm-debug-twin",
                Event = "timer",
                Status = "firing",
                Once = true,
                Port = 9222,
                WaitSeconds = 5
            };
            Assert.False(IdeIgniteArmHost.MirrorTimerWakeToIntercom(arm, "Resume idle-PF mirror."));
        }
        finally
        {
            IdeIgniteArmHost.BindPrimaryAutoiSeat(null);
        }
    }

    [Fact]
    public void TryDeliverHabitatWake_skips_prefer_autonomous_fdr_on_non_primary()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindPrimaryAutoiSeat(false);
        try
        {
            Assert.False(IdeIgniteArmHost.IsHabitatPartnerLive());
            var arm = new IdeIgniteArmHost.IgniteArm
            {
                Id = "arm-debug-prefer",
                Event = "timer",
                Status = "firing",
                Once = true,
                Port = 9222,
                WaitSeconds = 5
            };
            Assert.Null(IdeIgniteArmHost.TryDeliverHabitatWake(arm, "Resume autonomous habitat."));
        }
        finally
        {
            IdeIgniteArmHost.BindPrimaryAutoiSeat(null);
            IdeIgniteArmHost.BindAutonomous(null);
        }
    }

    [Fact]
    public void MirrorClaimKey_collapses_remount_family()
    {
        var a = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeRemountWake.ArmIdPrefix + "aaa",
            Event = "timer",
            ChargeMode = "remount"
        };
        var b = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeRemountWake.ArmIdPrefix + "bbb",
            Event = "timer",
            ChargeMode = "remount"
        };
        Assert.Equal("family:remount", IdeIgniteArmHost.MirrorClaimKey(a));
        Assert.Equal(IdeIgniteArmHost.MirrorClaimKey(a), IdeIgniteArmHost.MirrorClaimKey(b));
    }
}
