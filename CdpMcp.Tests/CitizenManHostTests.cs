#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenManHostTests
{
    [Fact]
    public void Route_man_alone()
    {
        var r = CitizenIntentRouter.RouteOne("man");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Man, r.Verb);
        Assert.Equal("man", r.Go);
        Assert.Null(r.Tool);
    }

    [Fact]
    public void Route_desk_cdp_and_manual()
    {
        var desk = CitizenIntentRouter.RouteOne("man_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Man, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_man");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Man, cdp.Verb);

        var manual = CitizenIntentRouter.RouteOne("manual");
        Assert.True(manual.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Man, manual.Verb);

        var keyed = CitizenIntentRouter.RouteOne("man tool=cdp_health");
        Assert.True(keyed.Ok);
        Assert.Equal("cdp_health", keyed.Tool);

        var positional = CitizenIntentRouter.RouteOne("cdp_man context_budget");
        Assert.True(positional.Ok);
        Assert.Equal("context_budget", positional.Tool);
    }

    [Fact]
    public void Route_does_not_steal_go_health()
    {
        var go = CitizenIntentRouter.RouteOne("go=health");
        Assert.NotEqual(CitizenIntentRouter.Verb.Man, go.Verb);
    }

    [Fact]
    public void Execute_man_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.ManDispatchOverride = _ =>
                "TOC: cdp_cockpit (hub where-am-I), cdp_session";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("man")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("man", applied[0].Action);
            Assert.Equal("man", applied[0].Go);
            Assert.Contains("TOC", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ManDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_man_passes_tool()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.ManDispatchOverride = args =>
            {
                seen = args;
                return "Manual: cdp_health — see tool description; domain ops via prefixed tools / sibling man.";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_man tool=cdp_health")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("cdp_health", seen!["tool"].GetString());
            Assert.Contains("Manual", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ManDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_man_context_budget()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.ManDispatchOverride = args =>
            {
                seen = args;
                return "Manual: context_budget — W/C/A cheat sheet.";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("man tool=context_budget")]);
            Assert.True(applied[0].Ok);
            Assert.Equal("context_budget", seen!["tool"].GetString());
        }
        finally
        {
            CitizenRouteHost.ManDispatchOverride = null;
        }
    }
}
