#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenTestPlanHostTests
{
    [Fact]
    public void Route_test_plan_alone_is_preview()
    {
        var r = CitizenIntentRouter.RouteOne("test_plan");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.TestPlan, r.Verb);
        Assert.Equal("preview", r.Op);
        Assert.Equal("test_plan", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("test_plan_desk");
        Assert.True(desk.Ok);
        Assert.Equal("preview", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_test_plan");
        Assert.True(cdp.Ok);
        Assert.Equal("preview", cdp.Op);

        var preview = CitizenIntentRouter.RouteOne("test_plan_preview filter=Foo");
        Assert.True(preview.Ok);
        Assert.Equal("preview", preview.Op);
        Assert.Equal("Foo", preview.Tool);

        var apply = CitizenIntentRouter.RouteOne("test_plan_apply failed_first=true");
        Assert.True(apply.Ok);
        Assert.Equal("apply", apply.Op);
        Assert.Equal("true", apply.NewString);

        var draft = CitizenIntentRouter.RouteOne("test_plan_draft");
        Assert.True(draft.Ok);
        Assert.Equal("preview", draft.Op);
    }

    [Fact]
    public void Route_unknown_op_refused()
    {
        var r = CitizenIntentRouter.RouteOne("test_plan op=nope");
        Assert.False(r.Ok);
        Assert.Equal("test_plan_op_unknown", r.Reason);
    }

    [Fact]
    public void Route_does_not_steal_bare_test()
    {
        var test = CitizenIntentRouter.RouteOne("test");
        Assert.Equal(CitizenIntentRouter.Verb.Test, test.Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.TestPlan, test.Verb);

        var preview = CitizenIntentRouter.RouteOne("preview");
        Assert.NotEqual(CitizenIntentRouter.Verb.TestPlan, preview.Verb);
    }

    [Fact]
    public void Execute_test_plan_preview_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.TestPlanDispatchOverride = _ =>
                """{"ok":true,"schema":"test_run/v0","pulse":"test_plan · preview"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("test_plan")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("test_plan", applied[0].Action);
            Assert.Equal("test_plan", applied[0].Go);
            Assert.Contains("test_plan", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.TestPlanDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_test_plan_passes_op_filter_failed_first()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.TestPlanDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"test_plan · apply"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne(
                    "test_plan_apply filter=FullyQualifiedName~CitizenTestPlanHostTests failed_first=false")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("apply", seen!["op"].GetString());
            Assert.Equal("FullyQualifiedName~CitizenTestPlanHostTests", seen["filter"].GetString());
            Assert.False(seen["failed_first"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.TestPlanDispatchOverride = null;
        }
    }
}
