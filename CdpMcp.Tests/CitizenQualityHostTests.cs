#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenQualityHostTests
{
    [Fact]
    public void Route_quality_alone()
    {
        var r = CitizenIntentRouter.RouteOne("quality");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Quality, r.Verb);
        Assert.Equal("quality", r.Go);
        Assert.Null(r.Scene);
    }

    [Fact]
    public void Route_aliases_and_scopes()
    {
        var gates = CitizenIntentRouter.RouteOne("gates");
        Assert.True(gates.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Quality, gates.Verb);

        var desk = CitizenIntentRouter.RouteOne("quality_desk");
        Assert.Equal(CitizenIntentRouter.Verb.Quality, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_quality");
        Assert.Equal(CitizenIntentRouter.Verb.Quality, cdp.Verb);

        var disk = CitizenIntentRouter.RouteOne("quality scope=disk limit=20");
        Assert.True(disk.Ok);
        Assert.Equal("disk", disk.Scene);
        Assert.Equal("20", disk.Detail);

        var compound = CitizenIntentRouter.RouteOne("quality_assert");
        Assert.True(compound.Ok);
        Assert.Equal("assert", compound.Scene);

        var map = CitizenIntentRouter.RouteOne("quality_map");
        Assert.Equal("disk", map.Scene);
    }

    [Fact]
    public void Route_does_not_steal_go_quality()
    {
        var go = CitizenIntentRouter.RouteOne("go=quality");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("quality", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Quality, go.Verb);
    }

    [Fact]
    public void Execute_quality_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.QualityHandleOverride = _ =>
                new { schema = "quality_gates/v0", ok = true, pulse = "gates ok", warn = 0, fail = 0 };

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("quality")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("quality", applied[0].Action);
            Assert.Equal("quality", applied[0].Go);
            Assert.Contains("gates ok", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.QualityHandleOverride = null;
        }
    }

    [Fact]
    public void Execute_quality_passes_scope_disk()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.QualityHandleOverride = args =>
            {
                seen = args;
                return new { schema = "quality_gates/v0", ok = true, scope = "disk", pulse = "disk · top=40" };
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("quality_disk limit=20")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("disk", seen!["scope"].GetString());
            Assert.Equal(20, seen["limit"].GetInt32());
            Assert.Contains("disk", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.QualityHandleOverride = null;
        }
    }

    [Fact]
    public void Execute_quality_assert_scope()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.QualityHandleOverride = args =>
            {
                seen = args;
                return new { ok = true, pulse = "assert · clean" };
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("quality scope=adx")]);
            Assert.True(applied[0].Ok);
            Assert.Equal("assert", seen!["scope"].GetString());
        }
        finally
        {
            CitizenRouteHost.QualityHandleOverride = null;
        }
    }

    [Fact]
    public void Execute_quality_error_board()
    {
        try
        {
            CitizenRouteHost.QualityHandleOverride = _ =>
                new { ok = false, error = "buffer_not_open", pulse = "quality · buffer_not_open" };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("quality path=Missing.cs")]);
            Assert.False(applied[0].Ok);
            Assert.Contains("buffer_not_open", applied[0].Reason ?? applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.QualityHandleOverride = null;
        }
    }

    [Fact]
    public void Execute_quality_gate_fail_still_host_ok()
    {
        try
        {
            CitizenRouteHost.QualityHandleOverride = _ =>
                new { schema = "quality_gates/v0", ok = false, scope = "disk", pulse = "disk FAIL×1 WARN×2", fail = 1, warn = 2 };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("quality scope=disk")]);
            Assert.True(applied[0].Ok);
            Assert.Contains("FAIL", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.QualityHandleOverride = null;
        }
    }
}
