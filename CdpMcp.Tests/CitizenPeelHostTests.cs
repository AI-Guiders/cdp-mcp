#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPeelHostTests
{
    [Fact]
    public void Route_peel_alone_is_place()
    {
        var r = CitizenIntentRouter.RouteOne("peel");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Peel, r.Verb);
        Assert.Equal("place", r.Op);
        Assert.Equal("peel", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_preview_args()
    {
        var desk = CitizenIntentRouter.RouteOne("peel_desk");
        Assert.True(desk.Ok);
        Assert.Equal("place", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_peel");
        Assert.True(cdp.Ok);
        Assert.Equal("place", cdp.Op);

        var preview = CitizenIntentRouter.RouteOne(
            "peel path=Foo.cs members=Bar out=Foo.Bar.cs apply=false");
        Assert.True(preview.Ok);
        Assert.Equal("preview", preview.Op);
        Assert.Equal("Foo.cs", preview.Path);
        Assert.Equal("Bar", preview.Tool);
        Assert.Equal("Foo.Bar.cs", preview.NewString);

        var apply = CitizenIntentRouter.RouteOne(
            "peel_apply path=Foo.cs members=Bar out=Foo.Bar.cs");
        Assert.True(apply.Ok);
        Assert.Equal("apply", apply.Op);
    }

    [Fact]
    public void Route_incomplete_args_fail()
    {
        var r = CitizenIntentRouter.RouteOne("peel path=Foo.cs members=Bar");
        Assert.False(r.Ok);
        Assert.Equal("peel_args_incomplete", r.Reason);
    }

    [Fact]
    public void Route_no_steal_bare_path_members_apply()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Peel, CitizenIntentRouter.RouteOne("path=x.cs").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Peel, CitizenIntentRouter.RouteOne("members=Foo").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Peel, CitizenIntentRouter.RouteOne("apply=true").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Peel, CitizenIntentRouter.RouteOne("out=x.cs").Verb);
    }

    [Fact]
    public void Execute_place_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("peel")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("peel", applied[0].Action);
            Assert.Contains("place", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_preview_passes_args()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.PeelHandleOverride = (_, _, args) =>
        {
            seen = args;
            return """{"ok":true,"pulse":"peel · preview · Foo.cs → Foo.Bar.cs"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("peel path=Foo.cs members=Bar out=Foo.Bar.cs")
            ]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("Foo.cs", seen!["path"].GetString());
            Assert.Equal("Bar", seen["members"].GetString());
            Assert.Equal("Foo.Bar.cs", seen["out"].GetString());
            Assert.False(seen["apply"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.PeelHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}