#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPs1HostTests
{
    [Fact]
    public void Route_ps1_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("ps1");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ps1, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("ps1_scene", r.Go);
    }

    [Fact]
    public void Route_ise_cdp_and_compounds()
    {
        var ise = CitizenIntentRouter.RouteOne("ise");
        Assert.True(ise.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ps1, ise.Verb);
        Assert.Equal("scene", ise.Op);

        var desk = CitizenIntentRouter.RouteOne("ps1_scene");
        Assert.True(desk.Ok);
        Assert.Equal("scene", desk.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_ps1_scene");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var put = CitizenIntentRouter.RouteOne("ps1_put name=probe.ps1");
        Assert.True(put.Ok);
        Assert.Equal("put", put.Op);
        Assert.Equal("probe.ps1", put.Path);

        var help = CitizenIntentRouter.RouteOne("ps1 help");
        Assert.True(help.Ok);
        Assert.Equal("help", help.Op);
    }

    [Fact]
    public void Route_no_steal_bare_run_put_open_check()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Run, CitizenIntentRouter.RouteOne("run path=App.csproj").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Put, CitizenIntentRouter.RouteOne("put path=tools/x.txt text=hi").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Ps1, CitizenIntentRouter.RouteOne("open").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Ps1, CitizenIntentRouter.RouteOne("check").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Ps1, CitizenIntentRouter.RouteOne("last").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Ps1, CitizenIntentRouter.RouteOne("help").Verb);
    }

    [Fact]
    public void Route_ps1_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("ps1 boom");
        Assert.False(r.Ok);
        Assert.Equal("ps1_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.Ps1DispatchOverride = _ =>
            """{"schema":"ps1_scene/v0","ok":true,"scene":"ps1","pulse":"ps1 ready — put"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("ps1")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("ps1", applied[0].Action);
            Assert.Contains("ps1", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.Ps1DispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_put_passes_name_and_text()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.Ps1DispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"ps1_scene/v0","ok":true,"op":"put","path":"D:\\tmp\\probe.ps1","pulse":"put ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("ps1 put name=probe text=\"Write-Host hi\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("put", seen!["op"].GetString());
            Assert.Equal("probe", seen["name"].GetString());
            Assert.Equal("Write-Host hi", seen["text"].GetString());
        }
        finally
        {
            CitizenRouteHost.Ps1DispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
