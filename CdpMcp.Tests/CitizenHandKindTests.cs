#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenHandKindTests
{
    [Fact]
    public void Classify_take_action_is_dig()
    {
        var a = new CitizenRouteHost.Applied(
            "@intent take",
            "Take",
            Ok: true,
            Action: "take",
            Go: "editor_scene",
            Pulse: "take chars=10 ship=10");
        Assert.Equal(CitizenHandKind.Dig, CitizenHandKindClassifier.Classify(a));
    }

    [Fact]
    public void Classify_replace_is_mutate()
    {
        var a = new CitizenRouteHost.Applied(
            "@intent replace",
            "Replace",
            Ok: true,
            Action: "replace",
            Go: "editor_scene");
        Assert.Equal(CitizenHandKind.Mutate, CitizenHandKindClassifier.Classify(a));
    }

    [Fact]
    public void Dominant_prefers_mutate_over_dig()
    {
        var dig = new CitizenRouteHost.Applied("t", "Take", Ok: true, Action: "take");
        var mut = new CitizenRouteHost.Applied("r", "Replace", Ok: true, Action: "replace");
        Assert.Equal(CitizenHandKind.Mutate, CitizenHandKindClassifier.Dominant([dig, mut]));
    }

    [Fact]
    public void SoftFL_ApplyArmed_skips_Dig_same_turn_observe_spam()
    {
        CitizenSoftFlLeaf.ResetForTests();
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.ArmApply(persist: false);
        var dig = CitizenPeerAck.FromExecuted(
        [
            new CitizenRouteHost.Applied("find", "find", true, "find", Pulse: "ok")
        ])!;
        Assert.Equal(CitizenHandKind.Dig, dig.HandKind);
        Assert.False(CitizenGlassDialogBridge.ShouldRunSameTurnObserve(dig));

        var mut = CitizenPeerAck.FromExecuted(
        [
            new CitizenRouteHost.Applied("edit", "edit", true, "edit", Pulse: "ok")
        ])!;
        Assert.True(CitizenGlassDialogBridge.ShouldRunSameTurnObserve(mut));

        CitizenSoftFlLeaf.DisarmApply(persist: false);
        Assert.True(CitizenGlassDialogBridge.ShouldRunSameTurnObserve(dig));
    }

    [Fact]
    public void SoftFlLeaf_apply_charge_has_ssot_not_dig_intent()
    {
        var charge = CitizenSoftFlLeaf.FormatApplyCharge(CitizenSoftFlLeaf.MentionsDefault);
        Assert.Contains("leaf SSOT", charge, StringComparison.Ordinal);
        Assert.Contains("MentionsAll", charge, StringComparison.Ordinal);
        Assert.DoesNotContain(CitizenSoftFlLeaf.FormatDigTakeIntent(CitizenSoftFlLeaf.MentionsDefault), charge, StringComparison.Ordinal);
        Assert.True(CitizenSoftFlLeaf.IsApplyWakeCharge(charge));
    }
}
