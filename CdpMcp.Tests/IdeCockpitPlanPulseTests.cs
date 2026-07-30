#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeCockpitPlanPulseTests
{
    [Fact]
    public void WantsDeskPulseFastPath_true_for_default_pulse()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
    }

    [Fact]
    public void WantsPlanPulseFastPath_true_for_plan_pulse_default()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Assert.True(IdeCockpit.WantsPlanPulseFastPath("plan", args));
    }

    [Fact]
    public void WantsDeskPulseFastPath_true_when_go_detail_full()
    {
        // go_detail=full = organ dump depth; must not force BuildAsync desk spray.
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["go_detail"] = JsonSerializer.SerializeToElement("full")
        };
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
        Assert.True(IdeCockpit.WantsPlanPulseFastPath("plan", args));
    }

    [Fact]
    public void WantsDeskPulseFastPath_true_when_seats_detail_full_alone()
    {
        // seats_detail=full without pane_full is W-spray refused — stay on desk-pulse
        // (do not enter TryGitAsync / ResolveSeatOrgan spray).
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["seats_detail"] = JsonSerializer.SerializeToElement("full")
        };
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
        Assert.True(IdeCockpit.WantsPlanPulseFastPath("plan", args));
    }

    [Fact]
    public void WantsPlanPulseFastPath_false_for_non_plan_but_desk_pulse_true()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        Assert.False(IdeCockpit.WantsPlanPulseFastPath("editor", args));
        Assert.False(IdeCockpit.WantsPlanPulseFastPath(null, args));
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
    }

    [Fact]
    public void DeskPulseFastPath_still_true_with_go_detail_full_for_alert_style_args()
    {
        // Regression: go=alert|chk + go_detail=full used to skip desk-pulse because deferred
        // soft wants forced full BuildAsync spray (~minutes). Desk-pulse gate must stay open;
        // deferred organs apply on cheap probes; glass spray skipped (CDP-ADR-0020).
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["go"] = JsonSerializer.SerializeToElement("alert"),
            ["go_detail"] = JsonSerializer.SerializeToElement("full")
        };
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
        Assert.False(IdeCockpit.WantsPlanPulseFastPath("alert", args));
    }

    [Fact]
    public void WantsDeskPulseFastPath_true_when_pane_full_set()
    {
        // pane_full= = one-seat dump on pulse — must not force TryGitAsync / all-seat spray.
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["pane_full"] = JsonSerializer.SerializeToElement("P")
        };
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(args));
        Assert.True(IdeCockpit.WantsPlanPulseFastPath("plan", args));
    }

    [Fact]
    public void Repl_done_routes_go_plan_for_fast_path()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["cmd"] = JsonSerializer.SerializeToElement("done")
        };
        var applied = IdeRepl.Apply("done", args);
        Assert.NotNull(applied);
        Assert.True(applied!.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("plan", go.GetString());
        Assert.True(IdeCockpit.WantsPlanPulseFastPath("plan", applied.Value.Args));
        Assert.True(IdeCockpit.WantsDeskPulseFastPath(applied.Value.Args));
    }
}
