#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCrmHostTests
{
    [Fact]
    public void Route_crm_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("crm");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Crm, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("crm", r.Go);
    }

    [Fact]
    public void Route_aliases_compounds_and_call()
    {
        var callout = CitizenIntentRouter.RouteOne("callout");
        Assert.True(callout.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Crm, callout.Verb);
        Assert.Equal("scene", callout.Op);

        var panel = CitizenIntentRouter.RouteOne("crm_panel");
        Assert.True(panel.Ok);
        Assert.Equal("scene", panel.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_crm");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var call = CitizenIntentRouter.RouteOne("crm call ask=Confirm approach");
        Assert.True(call.Ok);
        Assert.Equal("call", call.Op);

        var compound = CitizenIntentRouter.RouteOne("crm_respond code=go_around");
        Assert.True(compound.Ok);
        Assert.Equal("respond", compound.Op);
        Assert.Equal("go_around", compound.Path);

        var positional = CitizenIntentRouter.RouteOne("crm respond go_around");
        Assert.True(positional.Ok);
        Assert.Equal("respond", positional.Op);
        Assert.Equal("go_around", positional.Path);
    }

    [Fact]
    public void Route_no_steal_bare_lexicon_codes_or_scene()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Crm, CitizenIntentRouter.RouteOne("approved").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Crm, CitizenIntentRouter.RouteOne("go_around").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Crm, CitizenIntentRouter.RouteOne("lexicon").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Crm, CitizenIntentRouter.RouteOne("scene").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Crm, CitizenIntentRouter.RouteOne("call").Verb);
    }

    [Fact]
    public void Route_crm_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("crm boom");
        Assert.False(r.Ok);
        Assert.Equal("crm_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.CrmHandleOverride = (_, _, _, _) =>
            new { schema = "crm/v1", ok = true, op = "scene", pulse = "crm · idle" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("crm")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("crm", applied[0].Action);
            Assert.Contains("crm", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.CrmHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_respond_passes_code()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.CrmHandleOverride = (_, _, _, args) =>
        {
            seen = args;
            return new { schema = "crm/v1", ok = true, op = "respond", pulse = "crm · go_around" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("crm respond code=go_around")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("respond", seen!["op"].GetString());
            Assert.Equal("go_around", seen["code"].GetString());
        }
        finally
        {
            CitizenRouteHost.CrmHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
