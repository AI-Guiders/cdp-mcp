#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenDebugHostTests
{
    [Fact]
    public void Route_debug_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("debug");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Debug, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("debug", r.Go);
    }

    [Fact]
    public void Route_debug_bp_list()
    {
        var r = CitizenIntentRouter.RouteOne("debug bp_list");
        Assert.True(r.Ok);
        Assert.Equal("bp_list", r.Op);
    }

    [Fact]
    public void Route_debug_bp_add_requires_path_and_line()
    {
        var missingPath = CitizenIntentRouter.RouteOne("debug bp_add line=10");
        Assert.False(missingPath.Ok);
        Assert.Equal("debug_path_required", missingPath.Reason);

        var missingLine = CitizenIntentRouter.RouteOne("debug bp_add path=Foo.cs");
        Assert.False(missingLine.Ok);
        Assert.Equal("debug_line_required", missingLine.Reason);
    }

    [Fact]
    public void Route_debug_bp_add_ok()
    {
        var r = CitizenIntentRouter.RouteOne("debug bp_add path=Foo.cs line=12");
        Assert.True(r.Ok);
        Assert.Equal("bp_add", r.Op);
        Assert.Equal("Foo.cs", r.Path);
    }

    [Fact]
    public void Execute_debug_without_session_fails_no_session()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("debug")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("debug", applied[0].Action);
        Assert.Equal("no_session", applied[0].Reason);
    }

    [Fact]
    public void Execute_debug_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.DebugDispatchOverride = (_, _) =>
            Task.FromResult("""{"schema":"debug_scene/v0","ok":true,"active_dap":false,"breakpoints":[]}""");
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("debug scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("debug", applied[0].Action);
            Assert.Contains("debug scene", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
