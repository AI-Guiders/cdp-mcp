#nullable enable
using CdpMcp.Cockpit.Cds;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.Surface;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>CCU / catalog DeskWireParity asserts.</summary>
public sealed partial class DeskWireParityTests
{
    [Fact]
    public void OrganJsonPulseUnit_prefers_pulse_property()
    {
        var unit = new OrganJsonPulseUnit();
        var snap = unit.FromJson("""{"ok":true,"pulse":"ignite · armed","schema":"ignite/v0","hint":"end turn"}""");
        Assert.True(snap.Ok);
        Assert.Equal("ignite · armed", snap.Line);
        Assert.Equal("ignite/v0", snap.Schema);
        Assert.Equal("end turn", snap.Hint);
    }

    [Fact]
    public void DeskLociBuildUnit_emits_core_loci()
    {
        var unit = new DeskLociBuildUnit();
        var loci = unit.Build(new DeskLociBuildUnit.Input(
            Session: new DeskLociBuildUnit.SessionFact(@"D:\proj", "csharp", new { ok = true }),
            Settings: new DeskLociBuildUnit.SettingsFact("settings ok", true, 1, "u", "p"),
            Git: new DeskLociBuildUnit.GitFact(true, true, "main", new { dirty = true }),
            ShellTabs: [],
            Browser: new DeskLociBuildUnit.BrowserFact(true, "browser · 1", "main", 1, null, null, null),
            Buffers: [],
            BufferCount: 0,
            Clipboard: null,
            Debug: new DeskLociBuildUnit.DebugFact(false, false, 0, new { }),
            Test: new DeskLociBuildUnit.TestFact(true, null, null, false, 0, 0, 0, new { }),
            Work: new DeskLociBuildUnit.WorkFact("plan · X", new { }),
            Quality: new DeskLociBuildUnit.QualityFact(true, 0, 0, "gates ok", new { }),
            Analysis: new DeskLociBuildUnit.AnalysisFact(true)));
        Assert.Contains(loci, l => l.Id == "session:project");
        Assert.Contains(loci, l => l.Id == "git:scm" && l.Pulse.Contains("dirty"));
        Assert.Contains(loci, l => l.Id == "buffer:none");
        Assert.Contains(loci, l => l.Id == "mfd:gates");
        Assert.Equal("Experiments/proj", DeskLociBuildUnit.ShortPath(@"D:\Experiments\proj"));
    }

    [Fact]
    public void DeskNextBuildUnit_caps_and_dedupes_go()
    {
        var unit = new DeskNextBuildUnit();
        var cards = unit.Build(new DeskNextBuildUnit.Input(
            HasProject: false,
            DeskBookmarkExists: false,
            WorkIntentId: null,
            WorkPulse: null,
            AlertBeeping: false,
            AlertPulse: null,
            PressureArmed: false,
            PressurePulse: null,
            ChkOpenRequired: 0,
            ChkPulse: null,
            PhaseReviewOrVerify: false,
            PhaseIsReview: false,
            QrhHotId: null,
            QrhPulse: null,
            LayoutHint: null,
            LayoutSeatNote: null,
            ProblemErrors: 0,
            AnyUndo: false,
            AnyClipboard: false,
            AnyNavBack: false,
            QualityEnabled: false,
            QualityFail: 0,
            QualityWarn: 0,
            SuggestSniper: false,
            SniperHasHold: false,
            SniperPulse: null,
            ArchHasWork: false,
            ArchPulse: null,
            ToolchainPulse: "toolchain",
            OnboardHasScan: false,
            OnboardPulse: null,
            DiskChangedCount: 0,
            FocusId: null,
            BufferCount: 0,
            BufferDirtyCount: 0,
            GitDirty: false,
            TestFailed: 0,
            DebugStopped: false,
            ShellRunning: 0));
        Assert.True(cards.Length <= DeskNextBuildUnit.Cap);
        Assert.Contains(cards, c => c.Go == "project_scene");
        Assert.Contains(cards, c => c.Go == "plan");
        Assert.Equal(cards.Length, cards.Select(c => c.Go).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void DeskSniperLocusUnit_null_without_hold()
    {
        var unit = new DeskSniperLocusUnit();
        Assert.Null(unit.TryBuild(new DeskSniperLocusUnit.Input(false, "aim x", new { })));
        var hit = unit.TryBuild(new DeskSniperLocusUnit.Input(true, "aim x", new { k = 1 }));
        Assert.NotNull(hit);
        Assert.Equal("edit:sniper", hit!.Value.Id);
        Assert.Equal("sniper", hit.Value.Kind);
        Assert.Equal("aim aim x", hit.Value.Pulse);
        Assert.Equal("target", hit.Value.Go);
    }

    [Fact]
    public void WorldSnapPaneUnit_builds_git_and_shell()
    {
        var unit = new WorldSnapPaneUnit();
        var h = new WorldSnapPaneUnit.Habitat(true, "clean (main)", 2, 1, true, "browser · 1", true, "mcp · ok");
        dynamic git = unit.Build("git_scene", h);
        Assert.True((bool)git.ok);
        Assert.Equal("git_scene", (string)git.go);
        Assert.Equal("clean (main)", (string)git.pulse);
        Assert.True((bool)git.world);
        dynamic shell = unit.Build("shell_scene", h);
        Assert.Contains("running", (string)shell.pulse);
    }

    [Fact]
    public void EditorSnapPaneUnit_pulse_from_counts()
    {
        var unit = new EditorSnapPaneUnit();
        dynamic empty = unit.Build(new EditorSnapPaneUnit.BufferCounts(0, 0, 0));
        Assert.Equal("—", (string)empty.pulse);
        dynamic dirty = unit.Build(new EditorSnapPaneUnit.BufferCounts(3, 2, 0));
        Assert.Equal("3 buf · dirty×2", (string)dirty.pulse);
    }

    [Fact]
    public void DeskSysOrganUnit_builds_pulse()
    {
        var unit = new DeskSysOrganUnit();
        dynamic board = unit.Build(new DeskSysOrganUnit.Input(
            ProjectRoot: @"D:\proj",
            OpsPulse: "ops · ok",
            GitPulse: "clean (main)",
            BufferCount: 2,
            BufferDirty: 1,
            BufferDiskChanged: 0,
            ShellTabCount: 1,
            ShellRunning: 0,
            ShellFailed: 0,
            DebugActiveDap: false,
            DebugStopped: false,
            DebugBreakpointCount: 0,
            TestAvailable: true,
            TestReason: null,
            TestLastRun: true,
            TestSuccess: true,
            TestPassed: 3,
            TestTotal: 3,
            WorkPulse: "plan · X"));
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
        Assert.False(cat.Contains("not_a_real_verb_xyz"));
    }
}
