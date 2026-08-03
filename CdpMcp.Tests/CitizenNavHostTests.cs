#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenNavHostTests
{
    [Fact]
    public void Route_back()
    {
        var r = CitizenIntentRouter.RouteOne("back");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Nav, r.Verb);
        Assert.Equal("back", r.Op);
    }

    [Fact]
    public void Route_forward()
    {
        var r = CitizenIntentRouter.RouteOne("forward");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Nav, r.Verb);
        Assert.Equal("forward", r.Op);
    }

    [Fact]
    public void Route_nav_and_recent()
    {
        var nav = CitizenIntentRouter.RouteOne("nav");
        Assert.True(nav.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Nav, nav.Verb);
        Assert.Equal("nav", nav.Op);

        var recent = CitizenIntentRouter.RouteOne("recent_files");
        Assert.True(recent.Ok);
        Assert.Equal("recent_files", recent.Op);

        var alias = CitizenIntentRouter.RouteOne("recent");
        Assert.True(alias.Ok);
        Assert.Equal("recent_files", alias.Op);
    }

    [Fact]
    public void Route_nav_op_keyed()
    {
        var r = CitizenIntentRouter.RouteOne("nav op=back");
        Assert.True(r.Ok);
        Assert.Equal("back", r.Op);
    }

    [Fact]
    public void Execute_back_passes_op_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.NavCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"back","locus":"[F:a.cs]","nav":{"back":1,"forward":0,"current":"[F:a.cs]"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("back")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("back", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("back", seen!["op"].GetString());
            Assert.Contains("back", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.NavCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_nav_empty_surfaces_error()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.NavCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"back","error":"nav_empty"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("back")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("nav_empty", applied[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.NavCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
