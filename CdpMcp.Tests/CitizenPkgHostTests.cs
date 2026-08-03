#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPkgHostTests
{
    [Fact]
    public void Route_pkg_alone_is_list()
    {
        var r = CitizenIntentRouter.RouteOne("pkg");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Pkg, r.Verb);
        Assert.Equal("list", r.Op);
        Assert.Equal("pkg", r.Go);
    }

    [Fact]
    public void Route_pkg_find_and_add()
    {
        var find = CitizenIntentRouter.RouteOne("pkg find query=Newtonsoft take=3");
        Assert.True(find.Ok);
        Assert.Equal("find", find.Op);
        Assert.Equal("Newtonsoft", find.Scene);
        Assert.Equal("3", find.Detail);

        var pos = CitizenIntentRouter.RouteOne("pkg find Serilog");
        Assert.True(pos.Ok);
        Assert.Equal("find", pos.Op);
        Assert.Equal("Serilog", pos.Scene);

        var add = CitizenIntentRouter.RouteOne("pkg add id=Newtonsoft.Json version=13.0.3");
        Assert.True(add.Ok);
        Assert.Equal("add", add.Op);
        Assert.Equal("Newtonsoft.Json", add.Tool);
        Assert.Equal("13.0.3", add.Detail);
    }

    [Fact]
    public void Route_pkg_unknown_and_missing_keys()
    {
        var bad = CitizenIntentRouter.RouteOne("pkg boom");
        Assert.False(bad.Ok);
        Assert.Equal("pkg_op_unknown", bad.Reason);

        var missQ = CitizenIntentRouter.RouteOne("pkg find");
        Assert.False(missQ.Ok);
        Assert.Equal("pkg_query_required", missQ.Reason);

        var missId = CitizenIntentRouter.RouteOne("pkg add");
        Assert.False(missId.Ok);
        Assert.Equal("pkg_id_required", missId.Reason);
    }

    [Fact]
    public void Route_nuget_and_compounds()
    {
        var nuget = CitizenIntentRouter.RouteOne("nuget outdated");
        Assert.True(nuget.Ok);
        Assert.Equal("outdated", nuget.Op);

        var compound = CitizenIntentRouter.RouteOne("pkg_list path=CdpMcp.csproj");
        Assert.True(compound.Ok);
        Assert.Equal("list", compound.Op);
        Assert.Equal("CdpMcp.csproj", compound.Path);
    }

    [Fact]
    public void Execute_pkg_list_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        string? seenTool = null;
        CitizenRouteHost.PkgDispatchOverride = (tool, _) =>
        {
            seenTool = tool;
            return """{"ok":true,"kind":"packages.list","summary":"ok","pulse":"pkg list ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("pkg list")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("pkg", applied[0].Action);
            Assert.Equal("cdp_pkg_list", seenTool);
            Assert.Contains("pkg", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.PkgDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_pkg_find_passes_query()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        string? tool = null;
        CitizenRouteHost.PkgDispatchOverride = (t, args) =>
        {
            tool = t;
            seen = args;
            return """{"ok":true,"kind":"packages.find","summary":"ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("pkg find query=xunit take=5")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("cdp_pkg_find", tool);
            Assert.NotNull(seen);
            Assert.Equal("xunit", seen!["query"].GetString());
            Assert.Equal(5, seen["take"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.PkgDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
