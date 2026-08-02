#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenMcpHostTests
{
    [Fact]
    public void Route_mcp_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("mcp");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Mcp, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("mcp", r.Go);
    }

    [Fact]
    public void Route_mcp_call_parses_server_tool()
    {
        var r = CitizenIntentRouter.RouteOne("mcp call server=time tool=get_current_time");
        Assert.True(r.Ok);
        Assert.Equal("call", r.Op);
        Assert.Equal("time", r.Server);
        Assert.Equal("get_current_time", r.Tool);
    }

    [Fact]
    public void Route_mcp_call_without_tool_fails()
    {
        var r = CitizenIntentRouter.RouteOne("mcp call server=time");
        Assert.False(r.Ok);
        Assert.Equal("mcp_tool_required", r.Reason);
    }

    [Fact]
    public void Execute_mcp_without_outlet_fails_no_outlet()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("mcp")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("mcp", applied[0].Action);
        Assert.Equal("no_outlet", applied[0].Reason);
    }

    [Fact]
    public void Execute_mcp_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.McpDispatchOverride = (_, _) =>
            Task.FromResult("""{"schema":"mcp_outlet/v1","ok":true,"op":"scene","count":0}""");
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("mcp scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("mcp", applied[0].Action);
            Assert.Contains("mcp scene", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
