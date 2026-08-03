#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenEditorSceneHostTests
{
    [Fact]
    public void Route_editor_scene_alone()
    {
        var r = CitizenIntentRouter.RouteOne("editor_scene");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.EditorScene, r.Verb);
        Assert.Equal("editor_scene", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_bare_editor()
    {
        var desk = CitizenIntentRouter.RouteOne("editor_scene_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.EditorScene, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_editor_scene");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.EditorScene, cdp.Verb);

        var bare = CitizenIntentRouter.RouteOne("editor");
        Assert.True(bare.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.EditorScene, bare.Verb);

        var path = CitizenIntentRouter.RouteOne("editor_scene path=CitizenRouteHost.cs detail=full");
        Assert.True(path.Ok);
        Assert.Equal("CitizenRouteHost.cs", path.Path);
        Assert.Equal("full", path.Op);
    }

    [Fact]
    public void Route_does_not_steal_bare_detail_or_open()
    {
        var open = CitizenIntentRouter.RouteOne("open path=CitizenRouteHost.cs");
        Assert.Equal(CitizenIntentRouter.Verb.Open, open.Verb);

        var detail = CitizenIntentRouter.RouteOne("detail=full");
        Assert.NotEqual(CitizenIntentRouter.Verb.EditorScene, detail.Verb);
    }

    [Fact]
    public void Execute_editor_scene_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.EditorSceneDispatchOverride = _ =>
                """{"ok":true,"schema":"editor_scene/v0","pulse":"editor · buffers=2"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("editor_scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("editor_scene", applied[0].Action);
            Assert.Equal("editor_scene", applied[0].Go);
            Assert.Contains("editor", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.EditorSceneDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_editor_scene_passes_path_detail()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.EditorSceneDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"editor_scene · map"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_editor_scene path=Foo.cs detail=map")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("Foo.cs", seen!["path"].GetString());
            Assert.Equal("map", seen["detail"].GetString());
        }
        finally
        {
            CitizenRouteHost.EditorSceneDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_editor_scene_locus_and_context()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.EditorSceneDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"editor_scene · map"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("editor locus=buffer:doc-1 context_lines=40")]);
            Assert.True(applied[0].Ok);
            Assert.Equal("buffer:doc-1", seen!["locus"].GetString());
            Assert.Equal(40, seen["context_lines"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.EditorSceneDispatchOverride = null;
        }
    }
}
