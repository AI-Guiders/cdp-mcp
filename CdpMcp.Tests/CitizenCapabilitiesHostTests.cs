#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCapabilitiesHostTests
{
    [Fact]
    public void Route_capabilities_alone()
    {
        var r = CitizenIntentRouter.RouteOne("capabilities");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Capabilities, r.Verb);
        Assert.Equal("capabilities", r.Go);
    }

    [Fact]
    public void Route_aliases()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Capabilities, CitizenIntentRouter.RouteOne("capabilities_desk").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Capabilities, CitizenIntentRouter.RouteOne("cdp_capabilities").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Capabilities, CitizenIntentRouter.RouteOne("caps").Verb);
    }

    [Fact]
    public void Route_does_not_steal_go_capabilities()
    {
        var go = CitizenIntentRouter.RouteOne("go=capabilities");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("capabilities", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Capabilities, go.Verb);
    }

    [Fact]
    public void Execute_capabilities_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.CapabilitiesDispatchOverride = _ =>
                """{"catalog":"f(phase)","domains":["git","build"],"affordances":42,"list_tools_count":10}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("capabilities")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("capabilities", applied[0].Action);
            Assert.Equal("capabilities", applied[0].Go);
            Assert.Contains("domains=2", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("aff=42", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.CapabilitiesDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_capabilities_error_board()
    {
        try
        {
            CitizenRouteHost.CapabilitiesDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("capabilities")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.CapabilitiesDispatchOverride = null;
        }
    }
}
