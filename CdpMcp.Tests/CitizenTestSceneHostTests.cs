#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenTestSceneHostTests
{
    [Fact]
    public void Route_test_scene_alone()
    {
        var r = CitizenIntentRouter.RouteOne("test_scene");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.TestScene, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("test_scene", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_runner()
    {
        var desk = CitizenIntentRouter.RouteOne("test_scene_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.TestScene, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_test_scene");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.TestScene, cdp.Verb);

        var runner = CitizenIntentRouter.RouteOne("test_runner");
        Assert.True(runner.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.TestScene, runner.Verb);

        var path = CitizenIntentRouter.RouteOne("test_scene path=CdpMcp.Tests max_tests=50");
        Assert.True(path.Ok);
        Assert.Equal("CdpMcp.Tests", path.Path);
        Assert.Equal("50", path.NewString);
    }

    [Fact]
    public void Route_does_not_steal_bare_test_or_test_plan()
    {
        var test = CitizenIntentRouter.RouteOne("test");
        Assert.Equal(CitizenIntentRouter.Verb.Test, test.Verb);

        var plan = CitizenIntentRouter.RouteOne("test_plan");
        Assert.Equal(CitizenIntentRouter.Verb.TestPlan, plan.Verb);

        var desk = CitizenIntentRouter.RouteOne("test_desk");
        Assert.NotEqual(CitizenIntentRouter.Verb.TestScene, desk.Verb);
    }

    [Fact]
    public void Execute_test_scene_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.TestSceneDispatchOverride = _ =>
                """{"ok":true,"schema":"test_scene/v0","pulse":"test_scene · discovered"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("test_scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("test_scene", applied[0].Action);
            Assert.Equal("test_scene", applied[0].Go);
            Assert.Contains("test_scene", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.TestSceneDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_test_scene_passes_path_max_tests()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.TestSceneDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"test_scene · map"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_test_scene path=CdpMcp.Tests max_tests=25")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("CdpMcp.Tests", seen!["path"].GetString());
            Assert.Equal(25, seen["max_tests"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.TestSceneDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_test_scene_configuration()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.TestSceneDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"test_scene · map"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("test_scene configuration=Release")]);
            Assert.True(applied[0].Ok);
            Assert.Equal("Release", seen!["configuration"].GetString());
        }
        finally
        {
            CitizenRouteHost.TestSceneDispatchOverride = null;
        }
    }
}
