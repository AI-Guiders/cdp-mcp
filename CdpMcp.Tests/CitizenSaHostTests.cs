#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSaHostTests
{
    [Fact]
    public void Route_sa_defaults_pulse()
    {
        var r = CitizenIntentRouter.RouteOne("sa");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Sa, r.Verb);
        Assert.Equal("pulse", r.Op);
        Assert.Equal("sa_desk", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("sa_desk").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("cdp_sa").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("code_sa").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("pre_sa").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("sa_code").Verb);
    }

    [Fact]
    public void Route_positional_and_keyed_depth()
    {
        Assert.Equal("pulse", CitizenIntentRouter.RouteOne("sa pulse").Op);
        Assert.Equal("full", CitizenIntentRouter.RouteOne("cdp_sa depth=full").Op);
        Assert.Equal("full", CitizenIntentRouter.RouteOne("sa_desk shape=full").Op);
    }

    [Fact]
    public void Route_locus_and_scope()
    {
        var r = CitizenIntentRouter.RouteOne("sa path=CitizenRouteHost.Sa.cs scope=file");
        Assert.True(r.Ok);
        Assert.Equal("CitizenRouteHost.Sa.cs", r.Path);
        Assert.Equal("file", r.Detail);
    }

    [Fact]
    public void Route_does_not_steal_go_sa_eicas()
    {
        var goSa = CitizenIntentRouter.RouteOne("go=sa");
        Assert.Equal(CitizenIntentRouter.Verb.Go, goSa.Verb);
        Assert.Equal("sa", goSa.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Sa, goSa.Verb);
    }

    [Fact]
    public void Route_unknown_depth_fails()
    {
        var r = CitizenIntentRouter.RouteOne("sa depth=boom");
        Assert.False(r.Ok);
        Assert.Equal("sa_depth_unknown", r.Reason);
    }

    [Fact]
    public void Route_positional_non_depth_is_locus()
    {
        var r = CitizenIntentRouter.RouteOne("sa CitizenRouteHost.Sa.cs");
        Assert.True(r.Ok);
        Assert.Equal("slim", r.Op);
        Assert.Equal("CitizenRouteHost.Sa.cs", r.Path);
    }

    [Fact]
    public void Execute_sa_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.SaDispatchOverride = _ =>
                """{"ok":true,"pulse":"sa_desk \u00B7 go \u00B7 0w/0f","verdict":"go"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("sa")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("sa", applied[0].Action);
            Assert.Equal("sa_desk", applied[0].Go);
            Assert.Contains("sa_desk", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.SaDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_sa_error_board()
    {
        try
        {
            CitizenRouteHost.SaDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("sa")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.SaDispatchOverride = null;
        }
    }
}
