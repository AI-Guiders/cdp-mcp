#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenAnalysisHostTests
{
    [Fact]
    public void Route_analysis_alone_is_map()
    {
        var r = CitizenIntentRouter.RouteOne("analysis");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Analysis, r.Verb);
        Assert.Equal("map", r.Op);
        Assert.Equal("analysis_scene", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("analysis_desk");
        Assert.True(desk.Ok);
        Assert.Equal("map", desk.Op);

        var scene = CitizenIntentRouter.RouteOne("analysis_scene");
        Assert.True(scene.Ok);
        Assert.Equal("map", scene.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_analysis_scene");
        Assert.True(cdp.Ok);
        Assert.Equal("map", cdp.Op);

        var clones = CitizenIntentRouter.RouteOne("analysis_clones scope=file path=Foo.cs");
        Assert.True(clones.Ok);
        Assert.Equal("clones", clones.Op);
        Assert.Equal("Foo.cs", clones.Path);

        var corr = CitizenIntentRouter.RouteOne("analysis_correspondence path=Foo.cs");
        Assert.True(corr.Ok);
        Assert.Equal("correspondence", corr.Op);

        var sem = CitizenIntentRouter.RouteOne("analysis_semantic_map path=Foo.cs mode=related");
        Assert.True(sem.Ok);
        Assert.Equal("semantic_map", sem.Op);
    }

    [Fact]
    public void Route_unknown_feature_refused()
    {
        var r = CitizenIntentRouter.RouteOne("analysis feature=nope");
        Assert.False(r.Ok);
        Assert.Equal("analysis_feature_unknown", r.Reason);
    }

    [Fact]
    public void Route_does_not_steal_bare_related_or_clones()
    {
        var related = CitizenIntentRouter.RouteOne("related");
        Assert.NotEqual(CitizenIntentRouter.Verb.Analysis, related.Verb);

        var clones = CitizenIntentRouter.RouteOne("clones");
        Assert.NotEqual(CitizenIntentRouter.Verb.Analysis, clones.Verb);
    }

    [Fact]
    public void Execute_analysis_map_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.AnalysisDispatchOverride = _ =>
                """{"ok":true,"schema":"analysis_scene/v0","pulse":"analysis ready","scene":"analysis"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("analysis")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("analysis", applied[0].Action);
            Assert.Equal("analysis_scene", applied[0].Go);
            Assert.Contains("analysis", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.AnalysisDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_analysis_clones_passes_feature_and_path()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.AnalysisDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"clones · file"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("analysis_clones scope=file path=CitizenRouteHost.Analysis.cs")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("clones", seen!["feature"].GetString());
            Assert.Equal("CitizenRouteHost.Analysis.cs", seen["path"].GetString());
            Assert.Equal("file", seen["scope"].GetString());
        }
        finally
        {
            CitizenRouteHost.AnalysisDispatchOverride = null;
        }
    }
}
