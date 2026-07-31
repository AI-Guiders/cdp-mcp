#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenIntentRouterTests
{
    [Fact]
    public void Go_intent_routes_to_go()
    {
        var r = CitizenIntentRouter.RouteOne("go=plan");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Go, r.Verb);
        Assert.Equal("plan", r.Go);
    }

    [Fact]
    public void Drill_editor_maps_go_editor_scene()
    {
        var r = CitizenIntentRouter.RouteOne("drill editor");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Drill, r.Verb);
        Assert.Equal("editor", r.Organ);
        Assert.Equal("editor_scene", r.Go);
    }

    [Fact]
    public void Open_path_routes_to_buffer()
    {
        var r = CitizenIntentRouter.RouteOne("open path=CitizenWireParser.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Open, r.Verb);
        Assert.Equal("CitizenWireParser.cs", r.Path);
        Assert.Equal("buffer", r.Go);
    }

    [Fact]
    public void W_spray_is_refused()
    {
        var r = CitizenIntentRouter.RouteOne("seats_detail=full");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refuse, r.Verb);
        Assert.Contains("refuse_w_spray", r.Reason!);
    }

    [Fact]
    public void RouteAll_skips_non_intent_messages()
    {
        var msgs = CitizenWireParser.Parse("""
            @intent go=alert
            @frame desk v0
            board | P:plan
            """);
        var routes = CitizenIntentRouter.RouteAll(msgs);
        Assert.Single(routes);
        Assert.Equal("alert", routes[0].Go);
    }

    [Fact]
    public void Drill_fixture_shape_routes()
    {
        var msgs = CitizenWireParser.Parse("""
            @intent drill editor

            @frame organ v0
            organ | editor
            cost  | C
            """);
        var routes = CitizenIntentRouter.RouteAll(msgs);
        Assert.Single(routes);
        Assert.Equal(CitizenIntentRouter.Verb.Drill, routes[0].Verb);
        Assert.Equal("editor_scene", routes[0].Go);
    }
}
