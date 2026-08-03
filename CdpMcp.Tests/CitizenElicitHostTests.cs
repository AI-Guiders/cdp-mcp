#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenElicitHostTests
{
    [Fact]
    public void Route_elicit_defaults_peek()
    {
        var r = CitizenIntentRouter.RouteOne("elicit");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Elicit, r.Verb);
        Assert.Equal("peek", r.Op);
        Assert.Equal("elicit", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Elicit, CitizenIntentRouter.RouteOne("cdp_elicit").Verb);
        Assert.Equal("peek", CitizenIntentRouter.RouteOne("cdp_elicit").Op);
    }

    [Fact]
    public void Route_positional_and_keyed_op()
    {
        Assert.Equal("ask", CitizenIntentRouter.RouteOne("elicit ask").Op);
        Assert.Equal("peek", CitizenIntentRouter.RouteOne("cdp_elicit op=caps").Op);
        Assert.Equal("ask", CitizenIntentRouter.RouteOne("elicit op=form").Op);
        Assert.Equal("peek", CitizenIntentRouter.RouteOne("elicit help").Op);
    }

    [Fact]
    public void Route_message_keyed()
    {
        var keyed = CitizenIntentRouter.RouteOne("elicit op=ask message=ship?");
        Assert.Equal("ask", keyed.Op);
        Assert.Equal("ship?", keyed.Detail);

        var positional = CitizenIntentRouter.RouteOne("elicit ask message=ship?");
        Assert.Equal("ask", positional.Op);
        Assert.Equal("ship?", positional.Detail);
    }

    [Fact]
    public void Route_does_not_steal_go_elicit()
    {
        var go = CitizenIntentRouter.RouteOne("go=elicit");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("elicit", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Elicit, go.Verb);
    }

    [Fact]
    public void Route_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("elicit op=boom");
        Assert.False(r.Ok);
        Assert.Equal("elicit_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_elicit_pulses()
    {
        try
        {
            CitizenRouteHost.ElicitDispatchOverride = _ =>
                """{"ok":true,"op":"peek","hint":"Client did not advertise elicitation — path 2 blocked at host."}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("elicit")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("elicit", applied[0].Action);
            Assert.Equal("elicit", applied[0].Go);
            Assert.Contains("elicit", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ElicitDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_elicit_error_board()
    {
        try
        {
            CitizenRouteHost.ElicitDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("elicit ask")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("elicit", applied[0].Action);
        }
        finally
        {
            CitizenRouteHost.ElicitDispatchOverride = null;
        }
    }
}
