#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCockpitHostHostTests
{
    [Fact]
    public void Route_cockpit_host_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("cockpit_host");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.CockpitHost, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("cockpit_host", r.Go);
    }

    [Fact]
    public void Route_compounds_map_start_stop()
    {
        var start = CitizenIntentRouter.RouteOne("cockpit_start");
        Assert.True(start.Ok);
        Assert.Equal("start", start.Op);

        var stop = CitizenIntentRouter.RouteOne("cockpit_stop path=C:\\glass.exe");
        Assert.True(stop.Ok);
        Assert.Equal("stop", stop.Op);

        // path on stop is harmless leftover; start carries path=
        var startPath = CitizenIntentRouter.RouteOne("cockpit_host start path=C:\\glass.exe");
        Assert.True(startPath.Ok);
        Assert.Equal("start", startPath.Op);
        Assert.Equal("C:\\glass.exe", startPath.Path);
    }

    [Fact]
    public void Route_no_steal_bare_start_or_stop()
    {
        var bareStart = CitizenIntentRouter.RouteOne("start");
        Assert.NotEqual(CitizenIntentRouter.Verb.CockpitHost, bareStart.Verb);

        var bareStop = CitizenIntentRouter.RouteOne("stop");
        Assert.NotEqual(CitizenIntentRouter.Verb.CockpitHost, bareStop.Verb);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.CockpitHostHandleOverride = _ =>
            """{"ok":true,"op":"scene","gui_host":"down","pulse":"cockpit_host · down · agent-only"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("cockpit_host")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("cockpit_host", applied[0].Action);
            Assert.Contains("cockpit_host", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.CockpitHostHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_start_passes_path()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.CockpitHostHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"start","gui_host":"up","pid":42,"pulse":"cockpit_host · up · pid=42"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cockpit_start path=C:\\glass.exe")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("start", seen!["op"].GetString());
            Assert.Equal("C:\\glass.exe", seen["path"].GetString());
        }
        finally
        {
            CitizenRouteHost.CockpitHostHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
