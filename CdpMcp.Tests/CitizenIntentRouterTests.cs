#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenIntentRouterTests
{
    static string ReadFixture(string name) => CitizenWireFixtureFiles.Read(name);

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
    public void Drill_corpus_fixture_routes_editor_scene()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("02-drill-editor.txt"));
        var routes = CitizenIntentRouter.RouteAll(msgs);
        Assert.Single(routes);
        Assert.Equal(CitizenIntentRouter.Verb.Drill, routes[0].Verb);
        Assert.Equal("editor", routes[0].Organ);
        Assert.Equal("editor_scene", routes[0].Go);
    }

    [Fact]
    public void Remount_corpus_fixture_has_no_intent_routes()
    {
        var msgs = CitizenWireParser.Parse(ReadFixture("03-remount-event.txt"));
        Assert.Contains(msgs, m => m.Kind == CitizenWireParser.Kind.Event);
        Assert.Empty(CitizenIntentRouter.RouteAll(msgs));
    }
}
