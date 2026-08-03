#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenLandHostTests
{
    [Fact]
    public void Route_land_alone_is_restore()
    {
        var r = CitizenIntentRouter.RouteOne("land");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Land, r.Verb);
        Assert.Equal("restore", r.Op);
        Assert.Equal("land", r.Go);
        Assert.Equal("[Family:navigation;Command:restore]", r.Command);
    }

    [Fact]
    public void Route_land_open_builds_nested_anchor()
    {
        var r = CitizenIntentRouter.RouteOne("land open path=CitizenRouteHost.cs line=50 member=RunLand");
        Assert.True(r.Ok);
        Assert.Equal("open", r.Op);
        Assert.Equal("CitizenRouteHost.cs", r.Path);
        Assert.Equal("50", r.Detail);
        Assert.Equal("RunLand", r.Scene);
        Assert.Contains("Command:open", r.Command, StringComparison.Ordinal);
        Assert.Contains("File:CitizenRouteHost.cs", r.Command, StringComparison.Ordinal);
        Assert.Contains("Line:50", r.Command, StringComparison.Ordinal);
        Assert.Contains("Member:RunLand", r.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Route_land_go_and_raw_wire()
    {
        var go = CitizenIntentRouter.RouteOne("land go go=editor_scene");
        Assert.True(go.Ok);
        Assert.Equal("go", go.Op);
        Assert.Equal("[Family:navigation;Command:go;Go:editor_scene]", go.Command);

        var wire = CitizenIntentRouter.RouteOne(
            "land anchor=\"[Family:navigation;Command:show;Anchor:[File:x.png]]\"");
        Assert.True(wire.Ok);
        Assert.Equal("show", wire.Op);
        Assert.Contains("Command:show", wire.Command, StringComparison.Ordinal);
    }

    [Fact]
    public void Route_land_unknown_and_missing_path()
    {
        var bad = CitizenIntentRouter.RouteOne("land boom");
        Assert.False(bad.Ok);
        Assert.Equal("land_op_unknown", bad.Reason);

        var miss = CitizenIntentRouter.RouteOne("land open");
        Assert.False(miss.Ok);
        Assert.Equal("land_path_required", miss.Reason);
    }

    [Fact]
    public void Execute_land_restore_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.LandDispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"navigation_land/v1","ok":true,"command":"restore","pulse":"land restore ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("land restore")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("land", applied[0].Action);
            Assert.Contains("land", applied[0].Pulse, StringComparison.Ordinal);
            Assert.NotNull(seen);
            Assert.Equal("[Family:navigation;Command:restore]", seen!["anchor"].GetString());
        }
        finally
        {
            CitizenRouteHost.LandDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_land_open_passes_built_anchor()
    {
        CitizenRouteHost.UnbindLifecycle();
        string? wire = null;
        CitizenRouteHost.LandDispatchOverride = args =>
        {
            wire = args["anchor"].GetString();
            return """{"schema":"navigation_land/v1","ok":true,"command":"open","result":{"path":"D:\\tmp\\CitizenRouteHost.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("land open path=CitizenRouteHost.cs")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("CitizenRouteHost.cs", Path.GetFileName(applied[0].Path));
            Assert.Equal("doc-1", applied[0].DocId);
            Assert.Contains("Command:open", wire, StringComparison.Ordinal);
            Assert.Contains("File:CitizenRouteHost.cs", wire, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.LandDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
