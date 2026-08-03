#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenIgniteHostTests
{
    [Fact]
    public void Route_ignite_alone_is_continuity()
    {
        var r = CitizenIntentRouter.RouteOne("ignite");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ignite, r.Verb);
        Assert.Equal("continuity", r.Op);
        Assert.Equal("ignite", r.Go);
    }

    [Fact]
    public void Route_ignite_arm_requires_when()
    {
        var missing = CitizenIntentRouter.RouteOne("ignite arm last_once=true");
        Assert.False(missing.Ok);
        Assert.Equal("ignite_when_required", missing.Reason);
    }

    [Fact]
    public void Route_ignite_arm_timer_ok()
    {
        var r = CitizenIntentRouter.RouteOne(
            "ignite arm when=timer in=3s last_once=true task=peer continuity insurance");
        Assert.True(r.Ok);
        Assert.Equal("arm", r.Op);
    }

    [Fact]
    public void Route_ignite_send_is_refuse()
    {
        var r = CitizenIntentRouter.RouteOne("ignite send");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Refuse, r.Verb);
        Assert.Equal("ignite_refuse_send", r.Reason);
    }

    [Fact]
    public void Execute_ignite_continuity_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IgniteHandleOverride = _ =>
            new { schema = "ignite/v0", ok = true, op = "continuity", pulse = "ignite · continuity · armed=1" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("ignite continuity")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("ignite", applied[0].Action);
            Assert.Contains("ignite continuity", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.IgniteHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_ignite_arm_passes_when_and_last_once()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.IgniteHandleOverride = args =>
        {
            seen = args;
            return new { schema = "ignite/v0", ok = true, op = "arm", pulse = "ignite · armed · timer · last_once" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("ignite arm when=timer in=3s last_once=true task=\"leaf insurance\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("arm", seen!["op"].GetString());
            Assert.Equal("timer", seen["when"].GetString());
            Assert.Equal("3s", seen["in"].GetString());
            Assert.True(seen["last_once"].GetBoolean());
            Assert.Equal("leaf insurance", seen["task"].GetString());
        }
        finally
        {
            CitizenRouteHost.IgniteHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
