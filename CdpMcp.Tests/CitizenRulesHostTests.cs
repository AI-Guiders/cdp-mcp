#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenRulesHostTests
{
    [Fact]
    public void Route_rules_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("rules");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Rules, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("rules", r.Go);
    }

    [Fact]
    public void Route_desk_standing_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("rules_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Rules, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var standing = CitizenIntentRouter.RouteOne("standing");
        Assert.True(standing.Ok);
        Assert.Equal("scene", standing.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_rules");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var card = CitizenIntentRouter.RouteOne("rules_card id=healthy-agent");
        Assert.True(card.Ok);
        Assert.Equal("card", card.Op);
        Assert.Equal("healthy-agent", card.Path);

        var pulse = CitizenIntentRouter.RouteOne("rules pulse focus=healthy-agent");
        Assert.True(pulse.Ok);
        Assert.Equal("pulse", pulse.Op);

        var positional = CitizenIntentRouter.RouteOne("rules card healthy-agent");
        Assert.True(positional.Ok);
        Assert.Equal("card", positional.Op);
        Assert.Equal("healthy-agent", positional.Path);
    }

    [Fact]
    public void Route_no_steal_bare_list_pulse_card_scene()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Rules, CitizenIntentRouter.RouteOne("list").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Rules, CitizenIntentRouter.RouteOne("pulse").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Rules, CitizenIntentRouter.RouteOne("card").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Rules, CitizenIntentRouter.RouteOne("scene").Verb);
    }

    [Fact]
    public void Route_rules_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("rules boom");
        Assert.False(r.Ok);
        Assert.Equal("rules_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.RulesHandleOverride = (_, _) =>
            new { schema = "rules_channel/v0", ok = true, op = "scene", pulse = "rules · 1 cards · healthy-agent" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("rules")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("rules", applied[0].Action);
            Assert.Contains("rules", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.RulesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_card_passes_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.RulesHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "rules_channel/v0", ok = true, op = "card", id = "healthy-agent", pulse = "rules · healthy-agent" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("rules card id=healthy-agent")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("card", seen!["op"].GetString());
            Assert.Equal("healthy-agent", seen["id"].GetString());
        }
        finally
        {
            CitizenRouteHost.RulesHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
