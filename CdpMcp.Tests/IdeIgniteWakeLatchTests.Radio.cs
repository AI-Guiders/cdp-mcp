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

    [Fact]
    public void FormatCitizenWakeIntercom_collapses_sa_frame_wall()
    {
        var arm = new IdeIgniteArmHost.IgniteArm
        {
            Id = IdeIgniteArmHost.LeafWakeArmId,
            Event = "timer",
            Task = "Dig densest Glass human flight"
        };
        var wall =
            "Света, спасибо за `@frame desk v0`. Вижу:\n\n" +
            "- **`tm | Shared-SSOT › Dig densest`**\n" +
            "- **`board | P:webcam_desk · F:editor · M:shell`**\n" +
            "…[truncated habitat wake]";
        var body = IdeIgniteArmHost.FormatCitizenWakeIntercom(arm, wall);
        Assert.Contains("→ PFD.NEXT", body, StringComparison.Ordinal);
        Assert.Contains("delta → Plan", body, StringComparison.Ordinal);
        Assert.DoesNotContain("board |", body, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated habitat wake", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LooksLikeHabitatRadioPointer_detects_autoi_remount_face()
    {
        Assert.True(IdeIgniteArmHost.LooksLikeHabitatRadioPointer(
            "Autoi \u00B7 remount\n\u2192 PFD.NEXT\ndelta \u2192 Plan \u00B7 remount-initialized"));
        Assert.False(IdeIgniteArmHost.LooksLikeHabitatRadioPointer(
            "\u0425\u0438\u0442\u0440\u0430\u044F \u0441\u0438\u0441\u0442\u0435\u043C\u0430 \u043E\u0431\u0445\u043E\u0434\u0430 — \u043B\u044E\u0431\u043B\u044E \u044D\u0442\u043E."));
    }
}
