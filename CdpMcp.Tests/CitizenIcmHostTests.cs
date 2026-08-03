#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenIcmHostTests
{
    [Fact]
    public void Route_icm_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("icm");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Icm, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("icm_desk", r.Go);
    }

    [Fact]
    public void Route_desk_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("icm_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Icm, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_icm");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var aliases = CitizenIntentRouter.RouteOne("icm_aliases");
        Assert.True(aliases.Ok);
        Assert.Equal("aliases", aliases.Op);

        var resolve = CitizenIntentRouter.RouteOne("icm resolve command_id=plan");
        Assert.True(resolve.Ok);
        Assert.Equal("resolve", resolve.Op);
        Assert.Equal("plan", resolve.Path);

        var invoke = CitizenIntentRouter.RouteOne("icm_invoke command_id=cdp_health");
        Assert.True(invoke.Ok);
        Assert.Equal("invoke", invoke.Op);
        Assert.Equal("cdp_health", invoke.Path);
    }

    [Fact]
    public void Route_no_steal_bare_run_list_aliases_resolve()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Run, CitizenIntentRouter.RouteOne("run path=App.csproj").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Icm, CitizenIntentRouter.RouteOne("list").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Icm, CitizenIntentRouter.RouteOne("aliases").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Icm, CitizenIntentRouter.RouteOne("resolve").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Icm, CitizenIntentRouter.RouteOne("invoke").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Icm, CitizenIntentRouter.RouteOne("scene").Verb);
    }

    [Fact]
    public void Route_icm_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("icm boom");
        Assert.False(r.Ok);
        Assert.Equal("icm_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IcmHandleOverride = _ =>
            """{"ok":true,"schema":"icm_channel/v1","pulse":"icm · bound=true · aliases=12"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("icm")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("icm", applied[0].Action);
            Assert.Contains("icm", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.IcmHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_resolve_passes_command_id()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.IcmHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"resolve","command_id":"plan","mapped":false,"pulse":"icm resolve ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("icm resolve command_id=plan")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("resolve", seen!["op"].GetString());
            Assert.Equal("plan", seen["command_id"].GetString());
        }
        finally
        {
            CitizenRouteHost.IcmHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
