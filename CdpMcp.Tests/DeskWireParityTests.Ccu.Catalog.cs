#nullable enable
using CdpMcp.Cockpit.Cds;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.Surface;
using Xunit;

namespace CdpMcp.Tests;
public sealed partial class DeskWireParityTests
{
    [Fact]
    public void DeskSysOrganUnit_builds_pulse()
    {
        var unit = new DeskSysOrganUnit();
        dynamic board = unit.Build(new DeskSysOrganUnit.Input(ProjectRoot: @"D:\proj", OpsPulse: "ops · ok", GitPulse: "clean (main)", BufferCount: 2, BufferDirty: 1, BufferDiskChanged: 0, ShellTabCount: 1, ShellRunning: 0, ShellFailed: 0, DebugActiveDap: false, DebugStopped: false, DebugBreakpointCount: 0, TestAvailable: true, TestReason: null, TestLastRun: true, TestSuccess: true, TestPassed: 3, TestTotal: 3, WorkPulse: "plan · X"));
        Assert.True((bool)board.ok);
        Assert.Equal("sys", (string)board.go);
        Assert.Contains("ops · ok", (string)board.pulse);
        Assert.Contains("buf=2", (string)board.pulse);
    }

    [Fact]
    public void DeskPlaceableOrganUnit_alias_and_scene()
    {
        var unit = new DeskPlaceableOrganUnit();
        var aliases = new DeskPinAliasCatalog().Map;
        Assert.True(unit.IsPlaceable("code_search", aliases));
        Assert.True(unit.IsPlaceable("script_scene", aliases));
        Assert.True(unit.IsPlaceable("ps1_scene", aliases));
        Assert.True(unit.IsPlaceable("ise", aliases));
        Assert.False(unit.IsPlaceable("clipboard", aliases));
    }

    [Fact]
    public void DeskPinAliasCatalog_canonicalizes()
    {
        var cat = new DeskPinAliasCatalog();
        Assert.Equal("editor_scene", cat.Canonical("code"));
        Assert.Equal("find_desk", cat.Canonical("code_search"));
        Assert.Equal("pressure_desk", cat.Canonical("pre_compact"));
        Assert.True(cat.Contains("sa"));
        Assert.Equal("alert", cat.Map["sa"]);
    }

    [Fact]
    public void DeskLayoutPresetCatalog_has_agent_desk()
    {
        var cat = new DeskLayoutPresetCatalog();
        Assert.True(cat.TryGet("agent", out var pins));
        Assert.Contains("plan", pins);
        Assert.Contains("editor_scene", pins);
        Assert.Contains(cat.Ids, id => id == "desk");
    }

    [Fact]
    public void DeskGoMapCatalog_resolves_verbs_and_defaults()
    {
        var cat = new DeskGoMapCatalog();
        Assert.True(cat.Contains("buffer"));
        Assert.True(cat.TryGet("toolchain_ensure", out var ensure));
        Assert.Equal("cdp_toolchain", ensure.Tool);
        Assert.NotNull(ensure.Defaults);
        Assert.Equal("ensure", ensure.Defaults!["op"].GetString());
        Assert.True(cat.TryGet("plan", out var plan));
        Assert.Equal("cdp_work", plan.Tool);
        Assert.True(cat.TryGet("land", out var land));
        Assert.Equal("cdp_land", land.Tool);
        Assert.True(cat.TryGet("navigate", out var navigate));
        Assert.Equal("cdp_land", navigate.Tool);
        Assert.True(cat.TryGet("icm", out var icm));
        Assert.Equal("cdp_icm", icm.Tool);
        Assert.False(cat.Contains("not_a_real_verb_xyz"));
    }
}