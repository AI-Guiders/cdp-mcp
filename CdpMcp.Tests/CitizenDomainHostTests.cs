#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenDomainHostTests
{
    [Fact]
    public void Route_domain_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("domain");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Domain, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("domain", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("domain_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Domain, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_domain");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var card = CitizenIntentRouter.RouteOne("domain_card id=citizen");
        Assert.True(card.Ok);
        Assert.Equal("card", card.Op);
        Assert.Equal("citizen", card.Path);

        var pulse = CitizenIntentRouter.RouteOne("domain pulse focus=tm");
        Assert.True(pulse.Ok);
        Assert.Equal("pulse", pulse.Op);

        var positional = CitizenIntentRouter.RouteOne("domain card citizen");
        Assert.True(positional.Ok);
        Assert.Equal("card", positional.Op);
        Assert.Equal("citizen", positional.Path);
    }

    [Fact]
    public void Route_no_steal_bare_list_pulse_card_scene()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Domain, CitizenIntentRouter.RouteOne("list").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Domain, CitizenIntentRouter.RouteOne("pulse").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Domain, CitizenIntentRouter.RouteOne("card").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Domain, CitizenIntentRouter.RouteOne("scene").Verb);
    }

    [Fact]
    public void Route_domain_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("domain boom");
        Assert.False(r.Ok);
        Assert.Equal("domain_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.DomainHandleOverride = (_, _) =>
            new { schema = "domain_channel/v0", ok = true, op = "scene", pulse = "domain · 3 cards · citizen" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("domain")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("domain", applied[0].Action);
            Assert.Contains("domain", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.DomainHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_card_passes_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.DomainHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "domain_channel/v0", ok = true, op = "card", id = "citizen", pulse = "domain · citizen" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("domain card id=citizen")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("card", seen!["op"].GetString());
            Assert.Equal("citizen", seen["id"].GetString());
        }
        finally
        {
            CitizenRouteHost.DomainHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
