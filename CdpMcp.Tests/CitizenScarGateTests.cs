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
        CitizenSoftFlLeaf.RootOverrideForTests = _root;
        CitizenScarLedger.RootOverrideForTests = _root;
        CitizenSoftFlLeaf.ResetForTests();
        CitizenScarLedger.ResetForTests();
    }

    public void Dispose()
    {
        CitizenSoftFlLeaf.ResetForTests();
        CitizenScarLedger.ResetForTests();
        CitizenSoftFlLeaf.RootOverrideForTests = null;
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
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.ArmApply();
        var routes = new[] { CitizenIntentRouter.RouteOne("take path=\"other.cs\" start_line=1 end_line=2") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.DoesNotContain(applied, a => a.Reason?.Contains(CitizenScarGate.RefusePathMutateOffLeaf, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Mutate_off_leaf_refused_when_apply_armed()
    {
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.ArmApply();
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
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.ArmApply();
        var leaf = CitizenSoftFlLeaf.Current.Path;

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
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.DisarmApply();
        var routes = new[] { CitizenIntentRouter.RouteOne("replace path=\"CascadeIDE.cs\" old=\"a\" new=\"b\"") };
        var applied = CitizenRouteHost.Execute(routes);
        Assert.DoesNotContain(applied, a => a.Reason?.Contains(CitizenScarGate.RefusePathMutateOffLeaf, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Force_escapes_off_leaf_refuse()
    {
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.ArmApply();
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
        CitizenSoftFlLeaf.EnsureMentionsDefault();
        CitizenSoftFlLeaf.DisarmApply();
        Assert.False(CitizenSoftFlLeaf.IsApplyArmed);
        _ = CitizenSoftFlLeaf.FormatApplyCharge();
        Assert.True(CitizenSoftFlLeaf.IsApplyArmed);
    }
}
