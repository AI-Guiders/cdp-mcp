#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenGotoAllHostTests
{
    [Fact]
    public void Route_cdp_goto_requires_query()
    {
        var r = CitizenIntentRouter.RouteOne("cdp_goto");
        Assert.False(r.Ok);
        Assert.Equal("goto_query_required", r.Reason);
    }

    [Fact]
    public void Route_cdp_goto_and_compounds()
    {
        var cdp = CitizenIntentRouter.RouteOne("cdp_goto query=CitizenRouteHost");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.GotoAll, cdp.Verb);
        Assert.Equal("CitizenRouteHost", cdp.Tool);
        Assert.Equal("goto", cdp.Go);

        var all = CitizenIntentRouter.RouteOne("goto_all query=Foo max=5");
        Assert.True(all.Ok);
        Assert.Equal("Foo", all.Tool);
        Assert.Equal("5", all.Detail);

        var feature = CitizenIntentRouter.RouteOne("goto_feature query=undo");
        Assert.True(feature.Ok);
        Assert.Equal("feature", feature.Op);
        Assert.Equal("undo", feature.Tool);

        var positional = CitizenIntentRouter.RouteOne("goto_all CitizenRouteHost");
        Assert.True(positional.Ok);
        Assert.Equal("CitizenRouteHost", positional.Tool);
    }

    [Fact]
    public void Route_bare_goto_without_path_is_goto_all()
    {
        var bare = CitizenIntentRouter.RouteOne("goto");
        Assert.False(bare.Ok);
        Assert.Equal("goto_query_required", bare.Reason);

        var fuzzy = CitizenIntentRouter.RouteOne("goto query=RunGotoAll");
        Assert.True(fuzzy.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.GotoAll, fuzzy.Verb);
    }

    [Fact]
    public void Route_does_not_steal_goto_definition()
    {
        var def = CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs line=10");
        Assert.True(def.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ide, def.Verb);
        Assert.Equal("go_to_definition", def.Op);
    }

    [Fact]
    public void Execute_goto_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.GotoAllDispatchOverride = _ =>
                """{"ok":true,"schema":"goto_all/v0","count":1,"pulse":"goto · 1 hit(s) Foo","hits":[{"name":"Foo"}]}""";

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_goto query=Foo")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("goto", applied[0].Action);
            Assert.Equal("goto", applied[0].Go);
            Assert.Contains("goto", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.GotoAllDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_goto_passes_query_kind_max()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.GotoAllDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"count":0,"hits":[]}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("goto_feature query=pressure max=8 peek=false")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("pressure", seen!["query"].GetString());
            Assert.Equal("feature", seen["kind"].GetString());
            Assert.Equal(8, seen["max"].GetInt32());
            Assert.False(seen["peek"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.GotoAllDispatchOverride = null;
        }
    }
}
