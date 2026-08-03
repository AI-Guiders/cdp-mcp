#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSoftOrganMetaHostBatch9Tests
{
    [Theory]
    [InlineData("report", "scene", "report")]
    [InlineData("report_board", "scene", "report")]
    [InlineData("cdp_report", "scene", "report")]
    [InlineData("debug_sa", "pulse", "debug_desk")]
    [InlineData("debug_desk depth=full", "full", "debug_desk")]
    [InlineData("cdp_debug_sa", "pulse", "debug_desk")]
    [InlineData("test_sa", "pulse", "test_desk")]
    [InlineData("test_desk", "pulse", "test_desk")]
    [InlineData("build_sa", "pulse", "build_desk")]
    [InlineData("build_desk", "pulse", "build_desk")]
    [InlineData("sys", "scene", "sys")]
    [InlineData("sys_organ", "scene", "sys")]
    [InlineData("ecl", "run", "ecl")]
    [InlineData("chk list", "list", "ecl")]
    [InlineData("cdp_ecl", "run", "ecl")]
    [InlineData("review", "board", "review")]
    [InlineData("review files", "files", "review")]
    [InlineData("cdp_review", "board", "review")]
    [InlineData("alert", "pulse", "alert")]
    [InlineData("eicas", "pulse", "alert")]
    [InlineData("cdp_alert", "pulse", "alert")]
    public void Route_aliases_and_ops(string raw, string expectedOp, string expectedGo)
    {
        var r = CitizenIntentRouter.RouteOne(raw);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(expectedGo, r.Go);
        Assert.Equal(expectedOp, r.Op);
    }

    [Fact]
    public void Route_host_verbs_beat_place_go()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Report, CitizenIntentRouter.RouteOne("report").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Alert, CitizenIntentRouter.RouteOne("alert").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Go, CitizenIntentRouter.RouteOne("go=report").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Go, CitizenIntentRouter.RouteOne("go=alert").Verb);
    }

    [Fact]
    public void Route_no_steal_bare_lifecycle_or_sa()
    {
        Assert.Equal(CitizenIntentRouter.Verb.Build, CitizenIntentRouter.RouteOne("build").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Test, CitizenIntentRouter.RouteOne("test").Verb);
        Assert.Equal(CitizenIntentRouter.Verb.Debug, CitizenIntentRouter.RouteOne("debug").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.DebugSa, CitizenIntentRouter.RouteOne("debug").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.TestSa, CitizenIntentRouter.RouteOne("test").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.BuildSa, CitizenIntentRouter.RouteOne("build").Verb);

        Assert.Equal(CitizenIntentRouter.Verb.Sa, CitizenIntentRouter.RouteOne("sa").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Alert, CitizenIntentRouter.RouteOne("sa").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Alert, CitizenIntentRouter.RouteOne("sa_desk").Verb);

        Assert.Equal(CitizenIntentRouter.Verb.Evidence, CitizenIntentRouter.RouteOne("evidence text=err").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Report, CitizenIntentRouter.RouteOne("evidence text=err").Verb);
    }

    [Fact]
    public void Route_unknown_sa_shape_fails()
    {
        var r = CitizenIntentRouter.RouteOne("debug_sa boom");
        Assert.False(r.Ok);
        Assert.Equal("debug_sa_shape_unknown", r.Reason);
    }

    [Fact]
    public void Execute_report_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ReportHandleOverride = (_, _) =>
            new { schema = "report_board/v1", ok = true, idle = true, pulse = "report · idle" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("report")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("report", applied[0].Go);
        }
        finally
        {
            CitizenRouteHost.ReportHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_debug_sa_passes_depth()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.DebugSaHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "debug_sa/v1", ok = true, pulse = "debug_desk · idle" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("debug_sa depth=full")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("full", seen!["depth"].GetString());
        }
        finally
        {
            CitizenRouteHost.DebugSaHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_alert_with_fused_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.AlertHandleOverride = (inputs, _) =>
            new { schema = "alert/v0", ok = true, level = "clear", pulse = "SA clear", lines = Array.Empty<string>() };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("alert")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("alert", applied[0].Action);
            Assert.Null(applied[0].Seat);
        }
        finally
        {
            CitizenRouteHost.AlertHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_ecl_ack_passes_args()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.EclHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "ecl_organ/v1", ok = true, pulse = "ecl · 0 open" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("ecl ack id=ship-push")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("ack", seen!["op"].GetString());
            Assert.Equal("ship-push", seen["id"].GetString());
        }
        finally
        {
            CitizenRouteHost.EclHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_review_board_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ReviewHandleOverride = (_, _) =>
            new { schema = "review/v0", ok = true, pulse = "review · idle" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("review")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("review", applied[0].Go);
        }
        finally
        {
            CitizenRouteHost.ReviewHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
