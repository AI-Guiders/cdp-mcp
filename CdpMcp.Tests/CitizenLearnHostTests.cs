#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenLearnHostTests
{
    [Fact]
    public void Route_learn_defaults_scene()
    {
        var r = CitizenIntentRouter.RouteOne("learn");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Learn, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("learn", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Learn, CitizenIntentRouter.RouteOne("learn_desk").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Learn, CitizenIntentRouter.RouteOne("cdp_learn").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Learn, CitizenIntentRouter.RouteOne("learning").Verb);
    }

    [Fact]
    public void Route_positional_and_keyed_op()
    {
        Assert.Equal("list", CitizenIntentRouter.RouteOne("learn list").Op);
        Assert.Equal("stash", CitizenIntentRouter.RouteOne("cdp_learn op=stash").Op);
        Assert.Equal("scene", CitizenIntentRouter.RouteOne("learn_desk op=help").Op);
        Assert.Equal("promote", CitizenIntentRouter.RouteOne("learn export").Op);
    }

    [Fact]
    public void Route_does_not_steal_go_learn()
    {
        var goLearn = CitizenIntentRouter.RouteOne("go=learn");
        Assert.Equal(CitizenIntentRouter.Verb.Go, goLearn.Verb);
        Assert.Equal("learn", goLearn.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Learn, goLearn.Verb);
    }

    [Fact]
    public void Route_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("learn op=boom");
        Assert.False(r.Ok);
        Assert.Equal("learn_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_learn_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.LearnDispatchOverride = _ =>
                """{"ok":true,"pulse":"learn · 1 card(s) · go=learn","count":1}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("learn")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("learn", applied[0].Action);
            Assert.Equal("learn", applied[0].Go);
            Assert.Contains("learn", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.LearnDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_learn_error_board()
    {
        try
        {
            CitizenRouteHost.LearnDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("learn")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.LearnDispatchOverride = null;
        }
    }
}
