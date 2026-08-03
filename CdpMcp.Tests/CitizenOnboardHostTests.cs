#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenOnboardHostTests
{
    [Fact]
    public void Route_onboard_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("onboard");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Onboard, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("onboard_desk", r.Go);
    }

    [Fact]
    public void Route_desk_explore_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("onboard_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Onboard, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var explore = CitizenIntentRouter.RouteOne("explore_desk");
        Assert.True(explore.Ok);
        Assert.Equal("scene", explore.Op);

        var bareExplore = CitizenIntentRouter.RouteOne("explore");
        Assert.True(bareExplore.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Onboard, bareExplore.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_onboard");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var scan = CitizenIntentRouter.RouteOne("onboard_scan");
        Assert.True(scan.Ok);
        Assert.Equal("scan", scan.Op);

        var clear = CitizenIntentRouter.RouteOne("onboard clear");
        Assert.True(clear.Ok);
        Assert.Equal("clear", clear.Op);
    }

    [Fact]
    public void Route_no_steal_bare_scene_scan_clear()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Onboard, CitizenIntentRouter.RouteOne("scene").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Onboard, CitizenIntentRouter.RouteOne("scan").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Onboard, CitizenIntentRouter.RouteOne("clear").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Onboard, CitizenIntentRouter.RouteOne("refresh").Verb);
    }

    [Fact]
    public void Route_onboard_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("onboard boom");
        Assert.False(r.Ok);
        Assert.Equal("onboard_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.OnboardHandleOverride = (_, _) =>
            """{"ok":true,"schema":"onboard/v0","pulse":"onboard · cdp-mcp · entry=12"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("onboard")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("onboard", applied[0].Action);
            Assert.Contains("onboard", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.OnboardHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_scan_passes_op()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.OnboardHandleOverride = (_, args) =>
        {
            seen = args;
            return """{"ok":true,"op":"scan","pulse":"onboard · scanned"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("onboard scan")
            ]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("scan", seen!["op"].GetString());
        }
        finally
        {
            CitizenRouteHost.OnboardHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
