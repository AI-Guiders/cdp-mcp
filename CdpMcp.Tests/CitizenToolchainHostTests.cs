#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenToolchainHostTests
{
    [Fact]
    public void Route_toolchain_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("toolchain");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Toolchain, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("toolchain", r.Go);
    }

    [Fact]
    public void Route_ensure_requires_id()
    {
        var bare = CitizenIntentRouter.RouteOne("toolchain ensure");
        Assert.False(bare.Ok);
        Assert.Equal("toolchain_id_required", bare.Reason);

        var ensure = CitizenIntentRouter.RouteOne("toolchain ensure id=python");
        Assert.True(ensure.Ok);
        Assert.Equal("ensure", ensure.Op);
        Assert.Equal("python", ensure.Tool);
    }

    [Fact]
    public void Route_compounds_and_no_steal_bare_ensure_or_lsp()
    {
        var compound = CitizenIntentRouter.RouteOne("toolchain_probe");
        Assert.True(compound.Ok);
        Assert.Equal("probe", compound.Op);

        var bareEnsure = CitizenIntentRouter.RouteOne("ensure id=python");
        Assert.NotEqual(CitizenIntentRouter.Verb.Toolchain, bareEnsure.Verb);

        var lsp = CitizenIntentRouter.RouteOne("lsp_ensure id=python");
        Assert.Equal(CitizenIntentRouter.Verb.Settings, lsp.Verb);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ToolchainHandleOverride = (_, _) =>
            """{"ok":true,"op":"scene","count":5}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("toolchain")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("toolchain", applied[0].Action);
            Assert.Contains("toolchain", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ToolchainHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_ensure_passes_id_and_via()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ToolchainHandleOverride = (_, args) =>
        {
            seen = args;
            return """{"ok":true,"op":"ensure","id":"python","status":"already_ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("toolchain_ensure id=python via=winget")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("ensure", seen!["op"].GetString());
            Assert.Equal("python", seen["id"].GetString());
            Assert.Equal("winget", seen["via"].GetString());
        }
        finally
        {
            CitizenRouteHost.ToolchainHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
