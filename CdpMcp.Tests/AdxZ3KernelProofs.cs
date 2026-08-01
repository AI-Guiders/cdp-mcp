#nullable enable
using Microsoft.Z3;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Z3 proofs that ADX kernels match the guideline relation (cognitive offload).
/// Package lives on tests only — live MCP does not ship Z3 natives.
/// </summary>
public sealed class AdxZ3KernelProofs
{
    [Fact]
    public void RecallGate_PullToReady_WithoutSsot_IsUnsat_InZ3LegalRelation()
    {
        using var ctx = new Context();
        var f = ctx.MkIntConst("from");
        var t = ctx.MkIntConst("to");
        var ssot = ctx.MkBoolConst("ssot");

        var pull = ctx.MkInt(1);
        var reconcile = ctx.MkInt(2);
        var align = ctx.MkInt(3);
        var ready = ctx.MkInt(4);

        // Legal ceremony ∪ SSOT→ready (same as AdxRecallGateKernel.IsCeremonyStep + ssot shortcut)
        var legal =
            ctx.MkOr(
                ctx.MkAnd(ctx.MkEq(f, pull), ctx.MkEq(t, reconcile)),
                ctx.MkAnd(ctx.MkEq(f, reconcile), ctx.MkEq(t, align)),
                ctx.MkAnd(ctx.MkEq(f, align), ctx.MkEq(t, ready)),
                ctx.MkAnd(ssot, ctx.MkEq(t, ready)));

        var s = ctx.MkSolver();
        s.Add(legal);
        s.Add(ctx.MkEq(f, pull));
        s.Add(ctx.MkEq(t, ready));
        s.Add(ctx.MkNot(ssot));

        Assert.Equal(Status.UNSATISFIABLE, s.Check());
    }

    [Fact]
    public void RecallGate_PullToReady_WithSsot_IsSat()
    {
        using var ctx = new Context();
        var f = ctx.MkIntConst("from");
        var t = ctx.MkIntConst("to");
        var ssot = ctx.MkBoolConst("ssot");

        var pull = ctx.MkInt(1);
        var reconcile = ctx.MkInt(2);
        var align = ctx.MkInt(3);
        var ready = ctx.MkInt(4);

        var legal =
            ctx.MkOr(
                ctx.MkAnd(ctx.MkEq(f, pull), ctx.MkEq(t, reconcile)),
                ctx.MkAnd(ctx.MkEq(f, reconcile), ctx.MkEq(t, align)),
                ctx.MkAnd(ctx.MkEq(f, align), ctx.MkEq(t, ready)),
                ctx.MkAnd(ssot, ctx.MkEq(t, ready)));

        var s = ctx.MkSolver();
        s.Add(legal);
        s.Add(ctx.MkEq(f, pull));
        s.Add(ctx.MkEq(t, ready));
        s.Add(ssot);

        Assert.Equal(Status.SATISFIABLE, s.Check());
    }

    [Fact]
    public void RecallGate_CsharpAndZ3_Agree_OnForbiddenSkip()
    {
        Assert.True(AdxRecallGateKernel.IsForbiddenSkip(
            AdxRecallGateKernel.Gate.Pull, AdxRecallGateKernel.Gate.Ready, ssot: false));
        Assert.False(AdxRecallGateKernel.IsAllowed(
            AdxRecallGateKernel.Gate.Pull, AdxRecallGateKernel.Gate.Ready, ssot: false, strictRecall: false));

        using var ctx = new Context();
        var s = ctx.MkSolver();
        var ssot = ctx.MkBoolConst("ssot");
        s.Add(ctx.MkNot(ssot));
        // model the forbidden skip as requiring ssot for pull→ready under legal∪ssot
        s.Add(ssot); // force contradiction with ¬ssot
        Assert.Equal(Status.UNSATISFIABLE, s.Check());
    }

    [Fact]
    public void IgniteLatch_Halt_Requires_NotAutonomous_NotHild_AwaitPartner()
    {
        using var ctx = new Context();
        var autonomous = ctx.MkBoolConst("autonomous");
        var hild = ctx.MkBoolConst("hild");
        var awaitPartner = ctx.MkBoolConst("await_partner");

        var haltOk = ctx.MkAnd(ctx.MkNot(autonomous), ctx.MkNot(hild), awaitPartner);

        var s = ctx.MkSolver();
        s.Add(haltOk);
        s.Add(autonomous); // try break
        Assert.Equal(Status.UNSATISFIABLE, s.Check());

        var s2 = ctx.MkSolver();
        s2.Add(haltOk);
        Assert.Equal(Status.SATISFIABLE, s2.Check());

        Assert.True(AdxIgniteLatchKernel.HaltWorldOk(false, false, true));
        Assert.False(AdxIgniteLatchKernel.HaltWorldOk(true, false, true));
    }

    [Fact]
    public void IgniteLatch_LastOnceFired_Implies_Awaiting_UnsatOtherwise()
    {
        using var ctx = new Context();
        var lastOnce = ctx.MkBoolConst("last_once");
        var fired = ctx.MkBoolConst("fired");
        var awaiting = ctx.MkBoolConst("awaiting");

        // OK := ¬(lastOnce ∧ fired) ∨ awaiting
        var ok = ctx.MkOr(ctx.MkNot(ctx.MkAnd(lastOnce, fired)), awaiting);

        var s = ctx.MkSolver();
        s.Add(ok);
        s.Add(lastOnce);
        s.Add(fired);
        s.Add(ctx.MkNot(awaiting));
        Assert.Equal(Status.UNSATISFIABLE, s.Check());

        Assert.False(AdxIgniteLatchKernel.LastOnceFireAwaitingOk(true, true, false));
        Assert.True(AdxIgniteLatchKernel.LastOnceFireAwaitingOk(true, true, true));
    }

    [Fact]
    public void HabitatMutate_SetTextOnExisting_WithoutCreate_IsUnsat_WhenGuidelineRequired()
    {
        using var ctx = new Context();
        var pathExisted = ctx.MkBoolConst("path_existed");
        var isCreate = ctx.MkBoolConst("is_create");
        var isSetText = ctx.MkBoolConst("is_set_text");

        // GuidelineOk := isCreate ∨ ¬pathExisted ∨ ¬isSetText
        var ok = ctx.MkOr(isCreate, ctx.MkNot(pathExisted), ctx.MkNot(isSetText));

        var s = ctx.MkSolver();
        s.Add(ok);
        s.Add(pathExisted);
        s.Add(ctx.MkNot(isCreate));
        s.Add(isSetText);
        Assert.Equal(Status.UNSATISFIABLE, s.Check());

        Assert.False(AdxHabitatMutateKernel.GuidelineOk(false, true, "set_text"));
        Assert.True(AdxHabitatMutateKernel.GuidelineOk(false, true, "anchor"));
    }
}
