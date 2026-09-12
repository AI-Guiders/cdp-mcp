#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenScarGateTests : IDisposable
{
    readonly string _root;

    public CitizenScarGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-scar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CitizenSoftFlApplyLatch.RootOverrideForTests = _root;
        CitizenScarLedger.RootOverrideForTests = _root;
        CitizenSoftFlApplyLatch.ResetForTests();
        CitizenScarLedger.ResetForTests();
    }

    public void Dispose()
    {
        CitizenSoftFlApplyLatch.ResetForTests();
        CitizenScarLedger.ResetForTests();
        CitizenSoftFlApplyLatch.RootOverrideForTests = null;
        CitizenScarLedger.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Dig_is_free_even_when_apply_armed()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.ArmApply();
        var routes = new[] { CitizenIntentRouter.RouteOne("take path=\"other.cs\" start_line=1 end_line=2") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.DoesNotContain(applied, a => a.Reason?.Contains(CitizenScarGate.RefusePathMutateOffLeaf, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Mutate_off_leaf_refused_when_apply_armed()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.ArmApply();
        CitizenScarLedger.EnsureBuiltins();

        var routes = new[] { CitizenIntentRouter.RouteOne("replace path=\"CascadeIDE.cs\" old=\"a\" new=\"b\"") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Contains(CitizenScarGate.RefusePathMutateOffLeaf, applied[0].Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_on_leaf_allowed_when_apply_armed()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.ArmApply();
        var leaf = CitizenSoftFlApplyLatch.Current.Path;

        // Will fail later without doc store — but must not scar-refuse off-leaf.
        var routes = new[] { CitizenIntentRouter.RouteOne("replace path=\"" + leaf + "\" old=\"a\" new=\"b\"") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.Single(applied);
        Assert.DoesNotContain(CitizenScarGate.RefusePathMutateOffLeaf, applied[0].Reason ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(CitizenScarGate.RefuseMutateWithoutLeaf, applied[0].Reason ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Mutate_off_leaf_free_when_apply_disarmed()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.DisarmApply();
        var routes = new[] { CitizenIntentRouter.RouteOne("replace path=\"CascadeIDE.cs\" old=\"a\" new=\"b\"") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.DoesNotContain(applied, a => a.Reason?.Contains(CitizenScarGate.RefusePathMutateOffLeaf, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Force_escapes_off_leaf_refuse()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.ArmApply();
        var routes = new[] { CitizenIntentRouter.RouteOne("replace path=\"CascadeIDE.cs\" old=\"a\" new=\"b\" force=true") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.DoesNotContain(applied, a => a.Reason?.Contains(CitizenScarGate.RefusePathMutateOffLeaf, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Dogfood_promote_arms_scar_in_ledger()
    {
        CitizenResultWake.PromoteSoftFlDogfoodScar("mentions-all-resolve-wakes");
        Assert.True(CitizenScarLedger.IsArmed(CitizenScarLedger.ScarPathMutateOffLeaf));
        var snap = CitizenScarLedger.Snapshot();
        Assert.Contains(snap, s => s.Id == CitizenScarLedger.ScarPathMutateOffLeaf && s.Source == "dogfood");
    }

    [Fact]
    public void FormatApplyCharge_arms_blast_gate()
    {
        CitizenSoftFlApplyLatch.EnsureDefaultScope();
        CitizenSoftFlApplyLatch.DisarmApply();
        Assert.False(CitizenSoftFlApplyLatch.IsApplyArmed);
        _ = CitizenSoftFlApplyLatch.FormatApplyCharge();
        Assert.True(CitizenSoftFlApplyLatch.IsApplyArmed);
    }
}
