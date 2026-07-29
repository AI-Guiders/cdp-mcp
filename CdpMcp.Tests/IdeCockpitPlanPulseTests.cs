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
    public void WantsPlanPulseFastPath_false_when_go_detail_full()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["go_detail"] = JsonSerializer.SerializeToElement("full")
        };
        Assert.False(IdeCockpit.WantsPlanPulseFastPath("plan", args));
        Assert.False(IdeCockpit.WantsDeskPulseFastPath(args));
    }

    [Fact]
    public void WantsPlanPulseFastPath_false_when_seats_detail_full()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["seats_detail"] = JsonSerializer.SerializeToElement("full")
        };
        Assert.False(IdeCockpit.WantsPlanPulseFastPath("plan", args));
        Assert.False(IdeCockpit.WantsDeskPulseFastPath(args));
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
    public void WantsPlanPulseFastPath_false_when_pane_full_set()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["pane_full"] = JsonSerializer.SerializeToElement("P")
        };
        Assert.False(IdeCockpit.WantsPlanPulseFastPath("plan", args));
        Assert.False(IdeCockpit.WantsDeskPulseFastPath(args));
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
