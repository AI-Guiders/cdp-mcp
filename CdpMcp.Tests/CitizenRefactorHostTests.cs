#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenRefactorHostTests
{
    [Fact]
    public void Route_refactor_defaults_plan()
    {
        var r = CitizenIntentRouter.RouteOne("refactor");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refactor, r.Verb);
        Assert.Equal("plan", r.Op);
        Assert.Equal("refactor_plan", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Refactor, CitizenIntentRouter.RouteOne("refactor_plan").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Refactor, CitizenIntentRouter.RouteOne("cdp_refactor").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Refactor, CitizenIntentRouter.RouteOne("debt_scene").Verb);
    }

    [Fact]
    public void Route_positional_and_keyed_op()
    {
        Assert.Equal("debt", CitizenIntentRouter.RouteOne("refactor debt").Op);
        Assert.Equal("recommend", CitizenIntentRouter.RouteOne("cdp_refactor op=recommend").Op);
        Assert.Equal("plan", CitizenIntentRouter.RouteOne("refactor_plan op=help").Op);
        Assert.Equal("pulse", CitizenIntentRouter.RouteOne("refactor pulse").Op);
        Assert.Equal("partials", CitizenIntentRouter.RouteOne("refactor seam").Op);
    }

    [Fact]
    public void Route_does_not_steal_go_refactor_plan()
    {
        var go = CitizenIntentRouter.RouteOne("go=refactor_plan");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("refactor_plan", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Refactor, go.Verb);

        var goShort = CitizenIntentRouter.RouteOne("go=refactor");
        Assert.Equal(CitizenIntentRouter.Verb.Go, goShort.Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Refactor, goShort.Verb);
    }

    [Fact]
    public void Route_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("refactor op=boom");
        Assert.False(r.Ok);
        Assert.Equal("refactor_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_refactor_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.RefactorDispatchOverride = _ =>
                """{"ok":true,"pulse":"refactor_plan · hotspots=0 · idle","op":"plan"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("refactor")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("refactor", applied[0].Action);
            Assert.Equal("refactor_plan", applied[0].Go);
            Assert.Contains("refactor", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.RefactorDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_refactor_error_board()
    {
        try
        {
            CitizenRouteHost.RefactorDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("refactor pulse")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("refactor", applied[0].Action);
        }
        finally
        {
            CitizenRouteHost.RefactorDispatchOverride = null;
        }
    }
}
