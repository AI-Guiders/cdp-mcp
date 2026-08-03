#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenToolsHostTests
{
    [Fact]
    public void Route_tools_alone()
    {
        var r = CitizenIntentRouter.RouteOne("tools");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Tools, r.Verb);
        Assert.Equal("tools", r.Go);
        Assert.Null(r.Scene);
    }

    [Fact]
    public void Route_aliases_and_query_args()
    {
        var desk = CitizenIntentRouter.RouteOne("tools_desk");
        Assert.Equal(CitizenIntentRouter.Verb.Tools, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_tools");
        Assert.Equal(CitizenIntentRouter.Verb.Tools, cdp.Verb);

        var palette = CitizenIntentRouter.RouteOne("palette");
        Assert.Equal(CitizenIntentRouter.Verb.Tools, palette.Verb);

        var q = CitizenIntentRouter.RouteOne("tools phase=act object=code limit=5");
        Assert.True(q.Ok);
        Assert.Equal("act", q.Scene);
        Assert.Equal("code", q.Organ);
        Assert.Equal("5", q.Cmd);
    }

    [Fact]
    public void Route_does_not_steal_go_tools_or_tools_options()
    {
        var go = CitizenIntentRouter.RouteOne("go=tools");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("tools", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Tools, go.Verb);

        var opts = CitizenIntentRouter.RouteOne("tools_options");
        Assert.Equal(CitizenIntentRouter.Verb.Settings, opts.Verb);
    }

    [Fact]
    public void Execute_tools_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.ToolsDispatchOverride = _ =>
                """{"phase":"explore","object":"code","language":"csharp","total":10,"tools":[{"name":"git_scene"}]}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("tools")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("tools", applied[0].Action);
            Assert.Equal("tools", applied[0].Go);
            Assert.Contains("explore/code", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("n=10", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ToolsDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_tools_passes_query_args()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.ToolsDispatchOverride = args =>
            {
                seen = args;
                return """{"phase":"act","object":"code","total":3,"tools":[]}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_tools phase=act object=code intent=change limit=3")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("act", seen!["phase"].GetString());
            Assert.Equal("code", seen["object"].GetString());
            Assert.Equal("change", seen["intent"].GetString());
            Assert.Equal(3, seen["limit"].GetInt32());
            Assert.Contains("act/code", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ToolsDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_tools_error_board()
    {
        try
        {
            CitizenRouteHost.ToolsDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("tools")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.ToolsDispatchOverride = null;
        }
    }
}
