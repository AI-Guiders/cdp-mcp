#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenCalendarHostTests
{
    [Fact]
    public void Route_calendar_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("calendar");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Calendar, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("calendar", r.Go);
    }

    [Fact]
    public void Route_clock_alone_is_calendar_scene()
    {
        var r = CitizenIntentRouter.RouteOne("clock");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Calendar, r.Verb);
        Assert.Equal("scene", r.Op);
    }

    [Fact]
    public void Route_calendar_pulse_and_month()
    {
        var pulse = CitizenIntentRouter.RouteOne("calendar pulse");
        Assert.True(pulse.Ok);
        Assert.Equal("pulse", pulse.Op);

        var clockOp = CitizenIntentRouter.RouteOne("calendar clock");
        Assert.True(clockOp.Ok);
        Assert.Equal("pulse", clockOp.Op);

        var month = CitizenIntentRouter.RouteOne("calendar month");
        Assert.True(month.Ok);
        Assert.Equal("month", month.Op);

        var grid = CitizenIntentRouter.RouteOne("calendar grid");
        Assert.True(grid.Ok);
        Assert.Equal("month", grid.Op);
    }

    [Fact]
    public void Route_calendar_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("calendar boom");
        Assert.False(r.Ok);
        Assert.Equal("calendar_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_calendar_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.CalendarHandleOverride = (_, _) =>
            new { schema = "calendar_channel/v0", ok = true, op = "scene", pulse = "calendar · local ПН 2026-08-03 08:05 morning" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("calendar scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("calendar", applied[0].Action);
            Assert.Contains("calendar scene", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.CalendarHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_calendar_month_passes_op()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.CalendarHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "calendar_channel/v0", ok = true, op = "month", pulse = "calendar · local" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("calendar month")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("month", seen!["op"].GetString());
        }
        finally
        {
            CitizenRouteHost.CalendarHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
