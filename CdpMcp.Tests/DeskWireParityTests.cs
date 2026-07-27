#nullable enable
using CdpMcp.Cockpit.Cds;
using CdpMcp.Cockpit.Composition;
using CdpMcp.Cockpit.ComputingUnits;
using CdpMcp.Cockpit.DataBus;
using CdpMcp.Cockpit.Ids;
using CdpMcp.Cockpit.Instrument;
using CdpMcp.Cockpit.Surface;
using CdpMcp.Cockpit.Transport;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class DeskWireParityTests
{
    [Fact]
    public void DeskIngestionBus_publishes_on_channel()
    {
        using var bus = new DeskIngestionBus(8);
        Assert.True(bus.TryPublish(new DeskIngressEvent("t", "cmd", "sa", DateTimeOffset.UtcNow)));
        Assert.Equal(1, bus.Published);
        Assert.True(bus.Reader.TryRead(out var evt));
        Assert.Equal("t", evt.Source);
    }

    [Fact]
    public void AttentionRoutingUnit_forces_nav_desk_detail()
    {
        var unit = new AttentionRoutingUnit();
        var snap = unit.Compute(new AttentionRoutingUnit.Input("sys", "nav", true, null));
        Assert.Equal("nav", snap.Mfd);
        Assert.Null(snap.GoVerb);
        Assert.True(snap.DeskDetailNavForced);
    }

    [Fact]
    public void DeskInstrumentMountRegistry_syncs_seats()
    {
        var reg = new DeskInstrumentMountRegistry();
        reg.SyncFromSeats(new Dictionary<string, string?> { ["p"] = "plan", ["forward"] = "editor_scene", ["m"] = null },
            ["p", "forward", "m"]);
        var deck = reg.Describe();
        Assert.Equal(2, deck.OrderedInstrumentIds.Count);
        Assert.Contains("p:plan", deck.OrderedInstrumentIds);
    }

    [Fact]
    public void DeskDataBusHost_publishes_surface_built()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        DeskDataBusHost.Current.Publish(new DeskSurfaceBuiltEvent("seats", 3, null, DateTimeOffset.UtcNow));
        Assert.NotNull(got);
        Assert.Equal("seats", got!.Value.Mode);
        Assert.Equal(3, got.Value.SeatCount);
    }

    [Fact]
    public void DeskDetailUnit_focus_forces_nav()
    {
        var unit = new DeskDetailUnit();
        var snap = unit.Compute(new DeskDetailUnit.Input("slim", "buf"));
        Assert.Equal("nav", snap.DeskDetail);
        Assert.True(snap.WantNav);
    }

    [Fact]
    public void SeatsSurfaceCompositor_publishes_and_sets_desk_detail()
    {
        DeskSurfaceBuiltEvent? got = null;
        using var sub = DeskDataBusHost.Current.Subscribe<DeskSurfaceBuiltEvent>(e => got = e);
        var comp = new SeatsSurfaceCompositor();
        var decision = new DeskDetailUnit.Snapshot("nav", true);
        var dict = comp.Compose(
            new SeatsSurfaceScene(
                "cockpit/v1.20", "sa", new { }, new { }, new { },
                null, null, null, new { }, null,
                null, null, ["p"], ["agent"], null,
                Array.Empty<object>(), ["sa"]),
            new SeatsSurfacePayload(2),
            decision);
        Assert.Equal("nav", dict["desk_detail"]);
        Assert.NotNull(dict["loci"]);
        Assert.NotNull(got);
        Assert.Equal(2, got!.Value.SeatCount);
    }

    [Fact]
    public void FeatureSearchUnit_ranks_exact_prefix_contains()
    {
        var unit = new FeatureSearchUnit();
        var catalog = new (string Go, string Tool)[]
        {
            ("sa", "cockpit"),
            ("sys", "soft"),
            ("system", "soft"),
            ("plan", "tasks")
        };
        var hits = unit.Search("sys", 10, catalog);
        Assert.Equal(2, hits.Length);
        Assert.Equal("sys", hits[0].Go);
        Assert.Equal(1000, hits[0].Score);
        Assert.Equal("system", hits[1].Go);
        Assert.Equal(800, hits[1].Score);
    }

    [Fact]
    public void TilesSurfaceCompositor_sets_tiles_mode_without_seats()
    {
        var comp = new TilesSurfaceCompositor();
        var decision = new DeskDetailUnit.Snapshot("slim", false);
        var dict = comp.Compose(
            new TilesSurfaceScene(
                "cockpit/v1.20", "sa", new { }, new { pin = "git" }, null,
                new { }, null, null, null,
                ["git"], ["agent"], null, null),
            new TilesSurfacePayload(1),
            decision);
        Assert.Equal("tiles", dict["mode"]);
        Assert.Null(dict["seats"]);
        Assert.NotNull(dict["tiles"]);
        Assert.False(dict.ContainsKey("loci"));
    }

    [Fact]
    public void WorldSceneGoUnit_short_circuits_pulse_world_go()
    {
        var unit = new WorldSceneGoUnit();
        var yes = unit.Compute(new WorldSceneGoUnit.Input("git_scene", "pulse", false, true));
        Assert.True(yes.UseWorldSnap);
        Assert.Equal("git_scene", yes.Pin);
        var no = unit.Compute(new WorldSceneGoUnit.Input("git_scene", "full", false, true));
        Assert.False(no.UseWorldSnap);
    }

    [Fact]
    public void FocusLocusUnit_unknown_and_hit()
    {
        var unit = new FocusLocusUnit();
        var loci = new[] { new FocusLocusUnit.LocusRef("buf", "buffer", "open", "drill", "buffer", null) };
        Assert.Null(unit.Build(null, loci));
        dynamic miss = unit.Build("nope", loci)!;
        Assert.False((bool)miss.ok);
        dynamic hit = unit.Build("buf", loci)!;
        Assert.True((bool)hit.ok);
        Assert.Equal("buf", (string)hit.locus);
    }

    [Fact]
    public void GoVerbsCatalogUnit_merges_and_sorts()
    {
        var unit = new GoVerbsCatalogUnit();
        var verbs = unit.Merge(["zz", "sa"], ["aa"]);
        Assert.Equal(["aa", "sa", "zz"], verbs);
    }

    [Fact]
    public void SeatsDetailGateUnit_refuses_wspray_without_pane_full()
    {
        var unit = new SeatsDetailGateUnit();
        var snap = unit.Compute(new SeatsDetailGateUnit.Input("full", null, false, true));
        Assert.Equal("compact", snap.SeatsDetail);
        Assert.NotNull(snap.ThrashNote);
        Assert.False(snap.WantPanes);
    }

    [Fact]
    public void SeatOrganArgsSanitizer_strips_steer_keys()
    {
        var sanitizer = new SeatOrganArgsSanitizer();
        var args = new Dictionary<string, JsonElement>
        {
            ["go"] = JsonSerializer.SerializeToElement("browser"),
            ["op"] = JsonSerializer.SerializeToElement("scene")
        };
        var clean = sanitizer.Sanitize(args, wantFull: false);
        Assert.False(clean.ContainsKey("go"));
        Assert.True(clean.ContainsKey("op"));
        Assert.Equal("pulse", clean["go_detail"].GetString());
    }

    [Fact]
    public void GoResultSlimUnit_keeps_full_and_slims_fat()
    {
        var unit = new GoResultSlimUnit();
        var fat = new { ok = true, go = "arch_desk", board = new { roles = 1 }, pulse = "x" };
        var kept = unit.Slim(fat, "full", _ => new GoResultSlimUnit.OrganPulseSnap(true, "p", null, null, null));
        Assert.Same(fat, kept);
        dynamic slimmed = unit.Slim(fat, "pulse", _ => new GoResultSlimUnit.OrganPulseSnap(true, "arch · ok", null, null, null))!;
        Assert.True((bool)slimmed.slimmed);
        Assert.Equal("arch · ok", (string)slimmed.pulse);
    }

    [Fact]
    public void SeatFullPaneMatchUnit_matches_seat_organ_alias()
    {
        var unit = new SeatFullPaneMatchUnit();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ed"] = "editor_scene" };
        Assert.True(unit.Matches("p", "p", "plan", aliases));
        Assert.True(unit.Matches("plan", "p", "plan", aliases));
        Assert.True(unit.Matches("ed", "f", "editor_scene", aliases));
        Assert.False(unit.Matches(null, "p", "plan", aliases));
    }

    [Fact]
    public void SeatOrganPanePresenter_full_or_passthrough()
    {
        var board = new { ok = true };
        Assert.Same(board, SeatOrganPanePresenter.FullOr(board, false, "x", "t"));
        dynamic full = SeatOrganPanePresenter.FullOr(board, true, "x", "t");
        Assert.Equal("full", (string)full.detail);
        Assert.Equal("x", (string)full.go);
    }

    [Fact]
    public void SoftOrganAliasCatalog_resolves_aliases()
    {
        var cat = new SoftOrganAliasCatalog();
        Assert.Equal(SoftOrganKind.Plan, cat.TryResolve("plan"));
        Assert.Equal(SoftOrganKind.Plan, cat.TryResolve("work"));
        Assert.Equal(SoftOrganKind.Plan, cat.TryResolve("promote"));
        Assert.Equal(SoftOrganKind.FindDesk, cat.TryResolve("code_search"));
        Assert.Equal(SoftOrganKind.FindDesk, cat.TryResolve("cdp_search"));
        Assert.Equal(SoftOrganKind.Toolchain, cat.TryResolve("toolchain_ensure"));
        Assert.Equal(SoftOrganKind.Toolchain, cat.TryResolve("toolchain_install"));
        Assert.Equal(SoftOrganKind.Ecl, cat.TryResolve("chk"));
        Assert.Null(cat.TryResolve("editor_scene"));
        Assert.Null(cat.TryResolve("git_scene"));
        Assert.Null(cat.TryResolve("arch_desk"));
    }

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

    [Fact]
    public void SoftOrganBoardHit_feeds_presenter()
    {
        var hit = new SoftOrganBoardHit(new { ok = true }, "armed", "pressure_channel/v1");
        var meta = new SoftOrganBoardMetaCatalog.Meta(
            "pressure_desk", "cdp_pressure", SoftOrganPresentMode.PulseLine, "hint");
        dynamic pulse = SeatOrganPanePresenter.Present(
            meta, false, hit.Board, hit.Pulse, hit.Schema);
        Assert.Equal("armed", (string)pulse.pulse);
        Assert.Equal("pressure_channel/v1", (string)pulse.schema);
    }

    [Fact]
    public void SoftOrganBoardMetaCatalog_covers_all_kinds()
    {
        var cat = new SoftOrganBoardMetaCatalog();
        foreach (SoftOrganKind kind in Enum.GetValues<SoftOrganKind>())
        {
            var m = cat.Require(kind);
            Assert.False(string.IsNullOrWhiteSpace(m.Go));
            Assert.False(string.IsNullOrWhiteSpace(m.Tool));
        }
        Assert.Equal("cdp_search", cat.Require(SoftOrganKind.FindDesk).Tool);
        Assert.Equal("plan", cat.Require(SoftOrganKind.Plan).Go);
        Assert.Equal(SoftOrganPresentMode.PulseLine, cat.Require(SoftOrganKind.PressureDesk).Mode);
        Assert.Equal(SoftOrganPresentMode.PulseWithResult, cat.Require(SoftOrganKind.Problems).Mode);
        Assert.Equal(SoftOrganPresentMode.FullOr, cat.Require(SoftOrganKind.Plan).Mode);
    }

    [Fact]
    public void SeatOrganPanePresenter_Present_modes()
    {
        var board = new { ok = true };
        var fullOr = new SoftOrganBoardMetaCatalog.Meta("x", "t");
        dynamic asIs = SeatOrganPanePresenter.Present(fullOr, false, board);
        Assert.True((bool)asIs.ok);
        dynamic wrapped = SeatOrganPanePresenter.Present(fullOr, true, board);
        Assert.Equal("full", (string)wrapped.detail);

        var pulseLine = new SoftOrganBoardMetaCatalog.Meta(
            "pressure_desk", "cdp_pressure", SoftOrganPresentMode.PulseLine, "hint");
        dynamic pulse = SeatOrganPanePresenter.Present(pulseLine, false, board, "armed", "pressure_channel/v1");
        Assert.Equal("pulse", (string)pulse.detail);
        Assert.Equal("armed", (string)pulse.pulse);
        Assert.Equal("hint", (string)pulse.hint);

        var withResult = new SoftOrganBoardMetaCatalog.Meta(
            "problems", "problems_channel", SoftOrganPresentMode.PulseWithResult);
        dynamic pr = SeatOrganPanePresenter.Present(withResult, false, board, "E×1");
        Assert.Equal("pulse", (string)pr.detail);
        Assert.Equal("E×1", (string)pr.pulse);
        Assert.NotNull(pr.result);
    }

    [Fact]
    public void SeatFallbackSnapUnit_classifies_pins()
    {
        var unit = new SeatFallbackSnapUnit();
        Assert.Equal(
            SeatFallbackSnapUnit.SnapKind.None,
            unit.Classify(new SeatFallbackSnapUnit.Input("git_scene", true, true)));
        Assert.Equal(
            SeatFallbackSnapUnit.SnapKind.World,
            unit.Classify(new SeatFallbackSnapUnit.Input("git_scene", false, true)));
        Assert.Equal(
            SeatFallbackSnapUnit.SnapKind.Editor,
            unit.Classify(new SeatFallbackSnapUnit.Input("buffer", false, false)));
        Assert.Equal(
            SeatFallbackSnapUnit.SnapKind.Script,
            unit.Classify(new SeatFallbackSnapUnit.Input("probe", false, false)));
        Assert.Equal(
            SeatFallbackSnapUnit.SnapKind.None,
            unit.Classify(new SeatFallbackSnapUnit.Input("unknown", false, false)));
    }

    [Fact]
    public void SeatOrganPanePresenter_pulse_or_full()
    {
        var board = new { ok = true };
        dynamic pulse = SeatOrganPanePresenter.PulseOrFull(
            false, board, "x", "t", "line", "sch", "hint");
        Assert.Equal("pulse", (string)pulse.detail);
        Assert.Equal("line", (string)pulse.pulse);
        dynamic full = SeatOrganPanePresenter.PulseOrFull(
            true, board, "x", "t", "line", "sch", "hint");
        Assert.Equal("full", (string)full.detail);
    }
    [Fact]
    public void BuildAsync_peels_WorldGo_and_LegacyTiles_exist()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.WorldGo.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.LegacyTiles.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Probes.cs")), root);
    }






}
