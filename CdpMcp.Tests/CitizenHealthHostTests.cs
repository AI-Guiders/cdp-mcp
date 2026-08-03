#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenHealthHostTests
{
    [Fact]
    public void Route_health_alone()
    {
        var r = CitizenIntentRouter.RouteOne("health");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Health, r.Verb);
        Assert.Equal("health", r.Go);
        Assert.Null(r.Tool);
    }

    [Fact]
    public void Route_desk_cdp_and_explain()
    {
        var desk = CitizenIntentRouter.RouteOne("health_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Health, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_health");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Health, cdp.Verb);

        var keyed = CitizenIntentRouter.RouteOne("health explain_tool=cdp_man");
        Assert.True(keyed.Ok);
        Assert.Equal("cdp_man", keyed.Tool);

        var positional = CitizenIntentRouter.RouteOne("cdp_health cdp_context");
        Assert.True(positional.Ok);
        Assert.Equal("cdp_context", positional.Tool);
    }

    [Fact]
    public void Route_does_not_steal_go_health()
    {
        var go = CitizenIntentRouter.RouteOne("go=health");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("health", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Health, go.Verb);
    }

    [Fact]
    public void Execute_health_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.HealthDispatchOverride = _ =>
                """{"ok":true,"ops_pulse":"ops · seat=cdp · self=0.5.615 · clear","runtime":{"version":"0.5.615"},"seats":{"lag":false}}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("health")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("health", applied[0].Action);
            Assert.Equal("health", applied[0].Go);
            Assert.Contains("ops", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.HealthDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_health_passes_explain_tool()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.HealthDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"ops_pulse":"ops · explain","runtime":{"version":"0.5.615"}}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_health explain_tool=cdp_man")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("cdp_man", seen!["explain_tool"].GetString());
        }
        finally
        {
            CitizenRouteHost.HealthDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_health_fallback_pulse_from_runtime()
    {
        try
        {
            CitizenRouteHost.HealthDispatchOverride = _ =>
                """{"ok":true,"runtime":{"version":"0.5.615"},"seats":{"lag":true}}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("health")]);
            Assert.True(applied[0].Ok);
            Assert.Contains("0.5.615", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("lag", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.HealthDispatchOverride = null;
        }
    }
}
