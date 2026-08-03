#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenWorkHostTests
{
    [Fact]
    public void Route_work_defaults_status()
    {
        var r = CitizenIntentRouter.RouteOne("work");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Work, r.Verb);
        Assert.Equal("status", r.Op);
        Assert.Equal("intent_workspace", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Work, CitizenIntentRouter.RouteOne("work_desk").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Work, CitizenIntentRouter.RouteOne("cdp_work").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Work, CitizenIntentRouter.RouteOne("intent_workspace").Verb);
    }

    [Fact]
    public void Route_positional_and_keyed_op()
    {
        Assert.Equal("intent_list", CitizenIntentRouter.RouteOne("work intent_list").Op);
        Assert.Equal("stage_list", CitizenIntentRouter.RouteOne("cdp_work op=stage_list").Op);
    }

    [Fact]
    public void Route_does_not_steal_go_work_or_plan()
    {
        var goWork = CitizenIntentRouter.RouteOne("go=work");
        Assert.Equal(CitizenIntentRouter.Verb.Go, goWork.Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Work, goWork.Verb);

        var goPlan = CitizenIntentRouter.RouteOne("go=plan");
        Assert.Equal(CitizenIntentRouter.Verb.Go, goPlan.Verb);
    }

    [Fact]
    public void Route_does_not_steal_cmd()
    {
        var cmd = CitizenIntentRouter.RouteOne("cmd=done");
        Assert.Equal(CitizenIntentRouter.Verb.Cmd, cmd.Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Work, cmd.Verb);
    }

    [Fact]
    public void Execute_work_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.WorkDispatchOverride = _ =>
                """{"database_path":"x.witdb","active_intent_title":"Leaf","active_stage_title":"Dig","active_scene_name":"main"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("work")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("work", applied[0].Action);
            Assert.Equal("intent_workspace", applied[0].Go);
            Assert.Contains("intent=Leaf", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stage=Dig", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.WorkDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_work_error_board()
    {
        try
        {
            CitizenRouteHost.WorkDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("work")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.WorkDispatchOverride = null;
        }
    }
}
