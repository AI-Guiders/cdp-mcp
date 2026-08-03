#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPressureHostTests
{
    [Fact]
    public void Route_pressure_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("pressure");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Pressure, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("pressure", r.Go);
    }

    [Fact]
    public void Route_pressure_stash_requires_body()
    {
        var missing = CitizenIntentRouter.RouteOne("pressure stash");
        Assert.False(missing.Ok);
        Assert.Equal("pressure_body_required", missing.Reason);
    }

    [Fact]
    public void Route_pressure_stash_body_ok()
    {
        var r = CitizenIntentRouter.RouteOne("pressure stash body=\"leaf continuity\"");
        Assert.True(r.Ok);
        Assert.Equal("stash", r.Op);
    }

    [Fact]
    public void Execute_pressure_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.PressureHandleOverride = (_, _) =>
            new { schema = "pressure_channel/v1", ok = true, op = "scene", pulse = "pressure · ARMED · stashed" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("pressure scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("pressure", applied[0].Action);
            Assert.Contains("pressure scene", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.PressureHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_pressure_stash_passes_body()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.PressureHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "pressure_channel/v1", ok = true, op = "stash", pulse = "pressure · ARMED · stashed" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("pressure stash body=\"axes AutoI next leaf\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("stash", seen!["op"].GetString());
            Assert.Equal("axes AutoI next leaf", seen["body"].GetString());
        }
        finally
        {
            CitizenRouteHost.PressureHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
