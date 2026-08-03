#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPresentationHostTests
{
    [Fact]
    public void Route_cide_presentation_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("cide_presentation");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Presentation, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("cide_presentation", r.Go);
    }

    [Fact]
    public void Route_set_requires_patch()
    {
        var bare = CitizenIntentRouter.RouteOne("cide_presentation set");
        Assert.False(bare.Ok);
        Assert.Equal("presentation_patch_required", bare.Reason);

        var set = CitizenIntentRouter.RouteOne("cide_presentation set topology=(P)(F)(M)");
        Assert.True(set.Ok);
        Assert.Equal("set", set.Op);
    }

    [Fact]
    public void Route_compounds_and_no_steal_bare_set_or_settings()
    {
        var compound = CitizenIntentRouter.RouteOne("presentation_set tier=cockpit");
        Assert.True(compound.Ok);
        Assert.Equal("set", compound.Op);

        var bareSet = CitizenIntentRouter.RouteOne("set topology=(P)(F)(M)");
        Assert.NotEqual(CitizenIntentRouter.Verb.Presentation, bareSet.Verb);

        var settings = CitizenIntentRouter.RouteOne("settings");
        Assert.Equal(CitizenIntentRouter.Verb.Settings, settings.Verb);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.PresentationHandleOverride = _ =>
            """{"ok":true,"op":"scene","topology":"(P)(F)(M)"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("cide_presentation")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("presentation", applied[0].Action);
            Assert.Contains("presentation", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.PresentationHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_set_passes_topology_and_tier()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.PresentationHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"set","topology":"(P)(F)(M)","tier":"cockpit"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cide_presentation set topology=(P)(F)(M) tier=cockpit")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("set", seen!["op"].GetString());
            Assert.Equal("(P)(F)(M)", seen["topology"].GetString());
            Assert.Equal("cockpit", seen["tier"].GetString());
        }
        finally
        {
            CitizenRouteHost.PresentationHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
