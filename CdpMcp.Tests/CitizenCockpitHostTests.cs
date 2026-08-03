#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCockpitHostTests
{
    [Fact]
    public void Route_cockpit_alone()
    {
        var r = CitizenIntentRouter.RouteOne("cockpit");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Cockpit, r.Verb);
        Assert.Equal("cockpit", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Cockpit, CitizenIntentRouter.RouteOne("cockpit_desk").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Cockpit, CitizenIntentRouter.RouteOne("cdp_cockpit").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Cockpit, CitizenIntentRouter.RouteOne("agent_desk").Verb);
    }

    [Fact]
    public void Route_parses_layout_and_pane_full()
    {
        var r = CitizenIntentRouter.RouteOne("cockpit layout=code+shell pane_full=p");
        Assert.Equal(CitizenIntentRouter.Verb.Cockpit, r.Verb);
        Assert.Equal("code+shell", r.Scene);
        Assert.Equal("p", r.Organ);
    }

    [Fact]
    public void Route_does_not_steal_go_cockpit()
    {
        var go = CitizenIntentRouter.RouteOne("go=cockpit");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("cockpit", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Cockpit, go.Verb);
    }

    [Fact]
    public void Route_does_not_steal_cockpit_host()
    {
        var host = CitizenIntentRouter.RouteOne("cockpit_host");
        Assert.Equal(CitizenIntentRouter.Verb.CockpitHost, host.Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Cockpit, host.Verb);

        var start = CitizenIntentRouter.RouteOne("cockpit_start");
        Assert.Equal(CitizenIntentRouter.Verb.CockpitHost, start.Verb);
    }

    [Fact]
    public void Execute_cockpit_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.CockpitDispatchOverride = _ =>
                """{"schema":"cockpit/v1.20","ok":true,"mode":"seats","seats":{"count":3,"slots":[1,2,3]},"alert":{"pulse":"sa · clear"}}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("cockpit")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("cockpit", applied[0].Action);
            Assert.Equal("cockpit", applied[0].Go);
            Assert.Contains("mode=seats", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("seats=3", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.CockpitDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_cockpit_error_board()
    {
        try
        {
            CitizenRouteHost.CockpitDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("cockpit")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.CockpitDispatchOverride = null;
        }
    }
}
