#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenScriptHostTests
{
    [Fact]
    public void Route_script_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("script");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Script, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("script", r.Go);
    }

    [Fact]
    public void Route_csx_alias()
    {
        var r = CitizenIntentRouter.RouteOne("csx");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Script, r.Verb);
        Assert.Equal("scene", r.Op);
    }

    [Fact]
    public void Route_script_put()
    {
        var r = CitizenIntentRouter.RouteOne("script put name=probe text=hello");
        Assert.True(r.Ok);
        Assert.Equal("put", r.Op);
        Assert.Equal("probe", r.Path);
    }

    [Fact]
    public void Route_script_put_compound()
    {
        var r = CitizenIntentRouter.RouteOne("script_put name=probe.csx");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Script, r.Verb);
        Assert.Equal("put", r.Op);
        Assert.Equal("probe.csx", r.Path);
    }

    [Fact]
    public void Route_bare_run_not_stolen_by_script()
    {
        var r = CitizenIntentRouter.RouteOne("run path=App.csproj");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Run, r.Verb);
    }

    [Fact]
    public void Route_bare_put_not_stolen_by_script()
    {
        var r = CitizenIntentRouter.RouteOne("put path=tools/x.txt text=hi");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Put, r.Verb);
    }

    [Fact]
    public void Execute_script_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ScriptDispatchOverride = _ =>
            """{"schema":"script_scene/v0","ok":true,"scene":"script","pulse":"scripts ready — put"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("script scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("script", applied[0].Action);
            Assert.Contains("script", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ScriptDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_script_put_passes_name_and_text()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ScriptDispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"script_scene/v0","ok":true,"op":"put","path":"D:\\tmp\\probe.csx","pulse":"put ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("script put name=probe text=hello-csx")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("put", seen!["op"].GetString());
            Assert.Equal("probe", seen["name"].GetString());
            Assert.Equal("hello-csx", seen["text"].GetString());
        }
        finally
        {
            CitizenRouteHost.ScriptDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_script_without_store_fails_doc_store_unbound()
    {
        CitizenRouteHost.UnbindLifecycle();
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("script")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("script", applied[0].Action);
            Assert.Equal("doc_store_unbound", applied[0].Reason);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
