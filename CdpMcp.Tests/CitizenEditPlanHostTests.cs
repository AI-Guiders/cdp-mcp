#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenEditPlanHostTests
{
    [Fact]
    public void Route_edit_plan_alone_is_draft()
    {
        var r = CitizenIntentRouter.RouteOne("edit_plan");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.EditPlan, r.Verb);
        Assert.Equal("draft", r.Op);
        Assert.Equal("edit_plan", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("edit_plan_desk");
        Assert.True(desk.Ok);
        Assert.Equal("draft", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_edit_plan");
        Assert.True(cdp.Ok);
        Assert.Equal("draft", cdp.Op);

        var draft = CitizenIntentRouter.RouteOne("edit_plan_draft sketch=fix path=Foo.cs");
        Assert.True(draft.Ok);
        Assert.Equal("draft", draft.Op);
        Assert.Equal("Foo.cs", draft.Path);
        Assert.Equal("fix", draft.Tool);

        var validate = CitizenIntentRouter.RouteOne(
            "edit_plan_validate yaml=\"- path: Foo.cs\"");
        Assert.True(validate.Ok);
        Assert.Equal("validate", validate.Op);
        Assert.Equal("- path: Foo.cs", validate.NewString);

        var apply = CitizenIntentRouter.RouteOne(
            "edit_plan_apply yaml=\"- path: Foo.cs\"");
        Assert.True(apply.Ok);
        Assert.Equal("apply", apply.Op);
    }

    [Fact]
    public void Route_validate_apply_need_yaml()
    {
        var v = CitizenIntentRouter.RouteOne("edit_plan op=validate");
        Assert.False(v.Ok);
        Assert.Equal("edit_plan_yaml_required", v.Reason);

        var a = CitizenIntentRouter.RouteOne("edit_plan_apply");
        Assert.False(a.Ok);
        Assert.Equal("edit_plan_yaml_required", a.Reason);
    }

    [Fact]
    public void Route_no_steal_bare_draft_validate_yaml()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.EditPlan, CitizenIntentRouter.RouteOne("draft").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.EditPlan, CitizenIntentRouter.RouteOne("validate").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.EditPlan, CitizenIntentRouter.RouteOne("apply").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.EditPlan, CitizenIntentRouter.RouteOne("yaml=x").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.EditPlan, CitizenIntentRouter.RouteOne("edit path=Foo.cs anchor=Bar text=z").Verb);
    }

    [Fact]
    public void Execute_draft_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EditPlanDispatchOverride = args =>
        {
            seen = args;
            return """{"ok":true,"pulse":"edit_plan · draft · n=0"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("edit_plan")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("edit_plan", applied[0].Action);
            Assert.Equal("edit_plan", applied[0].Go);
            Assert.NotNull(seen);
            Assert.Equal("draft", seen!["op"].GetString());
            Assert.Contains("draft", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.EditPlanDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_validate_passes_yaml()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EditPlanDispatchOverride = args =>
        {
            seen = args;
            return """{"ok":true,"pulse":"edit_plan · validate"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("edit_plan op=validate yaml=\"- path: Foo.cs\"")
            ]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("validate", seen!["op"].GetString());
            Assert.Equal("- path: Foo.cs", seen["yaml"].GetString());
        }
        finally
        {
            CitizenRouteHost.EditPlanDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
