using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgniteFireChargeComposeRulesTests
{
    static IdeIgniteArmHost.IgniteArm MinimalArm() => new()
    {
        Id = "arm-min",
        Event = "timer",
        ChargeMode = "minimal",
        Message = "ignored",
        Task = "leaf",
    };

    [Fact]
    public void ComposeFireCharge_minimal_arm_caps_tier_when_preflight_full()
    {
        IdeStageCycle.Unbind();
        try
        {
            var charge = InvokeCompose(MinimalArm());
            Assert.Contains("TM:", charge, StringComparison.Ordinal);
            Assert.Contains(IdeIgniteChannel.CanonicalComposerCharge, charge, StringComparison.Ordinal);
            Assert.DoesNotContain("Human-face axe", charge, StringComparison.Ordinal);
            Assert.DoesNotContain("World dig (research freedom", charge, StringComparison.Ordinal);
            Assert.Contains("Compaction/amnesia", charge, StringComparison.Ordinal);
        }
        finally
        {
            IdeStageCycle.Unbind();
        }
    }

    [Fact]
    public void ComposeFireCharge_minimal_uses_preflight_tm_line()
    {
        var charge = InvokeCompose(MinimalArm());
        Assert.Contains("TM:", charge, StringComparison.Ordinal);
        Assert.Contains(IdeIgniteChannel.CanonicalComposerCharge, charge, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeFireCharge_custom_expands_template()
    {
        var arm = MinimalArm();
        arm.ChargeMode = "custom";
        arm.Message = "custom {task} {event}";

        var charge = InvokeCompose(arm);
        Assert.Contains("custom leaf timer", charge, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeFireCharge_remount_includes_reason_lead()
    {
        var arm = MinimalArm();
        arm.ChargeMode = IdeRemountWake.ChargeMode;

        var charge = InvokeCompose(arm);
        Assert.Contains("reason=remount", charge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeFireCharge_escalate_includes_reason_lead()
    {
        var arm = MinimalArm();
        arm.ChargeMode = IdeIgniteArmHost.HildEscalateChargeMode;

        var charge = InvokeCompose(arm);
        Assert.Contains("reason=escalate", charge, StringComparison.OrdinalIgnoreCase);
    }

    static string InvokeCompose(IdeIgniteArmHost.IgniteArm arm) =>
        (string)typeof(IdeIgniteArmHost).GetMethod(
                "ComposeFireCharge",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [arm, true, "pulse", "detail"])!;
}
