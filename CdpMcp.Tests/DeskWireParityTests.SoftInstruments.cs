#nullable enable
using CdpMcp.Cockpit.Surface;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Soft-instrument / seat-presenter / Build peel file asserts (DeskWireParity).</summary>
public sealed partial class DeskWireParityTests
{
    [Fact]
    public void SeatInstrumentPanePresenter_full_or_passthrough()
    {
        var board = new { ok = true };
        Assert.Same(board, SeatInstrumentPanePresenter.FullOr(board, false, "x", "t"));
        dynamic full = SeatInstrumentPanePresenter.FullOr(board, true, "x", "t");
        Assert.Equal("full", (string)full.detail);
        Assert.Equal("x", (string)full.go);
    }

    [Fact]
    public void SoftInstrumentAliasCatalog_resolves_aliases()
    {
        var cat = new SoftInstrumentAliasCatalog();
        Assert.Equal(SoftInstrumentKind.Plan, cat.TryResolve("plan"));
        Assert.Equal(SoftInstrumentKind.Plan, cat.TryResolve("work"));
        Assert.Equal(SoftInstrumentKind.Plan, cat.TryResolve("promote"));
        Assert.Equal(SoftInstrumentKind.FindDesk, cat.TryResolve("code_search"));
        Assert.Equal(SoftInstrumentKind.FindDesk, cat.TryResolve("cdp_search"));
        Assert.Equal(SoftInstrumentKind.Toolchain, cat.TryResolve("toolchain_ensure"));
        Assert.Equal(SoftInstrumentKind.Toolchain, cat.TryResolve("toolchain_install"));
        Assert.Equal(SoftInstrumentKind.MdAuthor, cat.TryResolve("md_author"));
        Assert.Equal(SoftInstrumentKind.MdAuthor, cat.TryResolve("cdp_md_author"));
        Assert.Equal(SoftInstrumentKind.Learn, cat.TryResolve("learn"));
        Assert.Equal(SoftInstrumentKind.Learn, cat.TryResolve("cdp_learn"));
        Assert.Equal(SoftInstrumentKind.Domain, cat.TryResolve("domain"));
        Assert.Equal(SoftInstrumentKind.Domain, cat.TryResolve("cdp_domain"));
        Assert.Equal(SoftInstrumentKind.Domain, cat.TryResolve("ownership"));
        Assert.Equal(SoftInstrumentKind.ProjectSwitch, cat.TryResolve("project_switch"));
        Assert.Equal(SoftInstrumentKind.ProjectSwitch, cat.TryResolve("ps"));
        Assert.Equal(SoftInstrumentKind.ProjectSwitch, cat.TryResolve("cdp_scope"));
        Assert.Equal(SoftInstrumentKind.Ecl, cat.TryResolve("chk"));
        Assert.Equal(SoftInstrumentKind.ArchDesk, cat.TryResolve("arch_desk"));
        Assert.Equal(SoftInstrumentKind.ArchDesk, cat.TryResolve("board"));
        Assert.Equal(SoftInstrumentKind.ArchDesk, cat.TryResolve("cdp_arch"));
        Assert.Equal(SoftInstrumentKind.RefactorPlan, cat.TryResolve("refactor_plan"));
        Assert.Equal(SoftInstrumentKind.RefactorPlan, cat.TryResolve("debt_scene"));
        Assert.Equal(SoftInstrumentKind.Ps1Desk, cat.TryResolve("ps1_scene"));
        Assert.Equal(SoftInstrumentKind.Ps1Desk, cat.TryResolve("ise"));
        Assert.Equal(SoftInstrumentKind.Ps1Desk, cat.TryResolve("cdp_ps1_scene"));
        Assert.Equal(SoftInstrumentKind.Calendar, cat.TryResolve("calendar"));
        Assert.Equal(SoftInstrumentKind.Calendar, cat.TryResolve("clock"));
        Assert.Equal(SoftInstrumentKind.Calendar, cat.TryResolve("cdp_calendar"));
        Assert.Equal(SoftInstrumentKind.Rules, cat.TryResolve("rules"));
        Assert.Equal(SoftInstrumentKind.Rules, cat.TryResolve("cdp_rules"));
        Assert.Equal(SoftInstrumentKind.Rules, cat.TryResolve("standing"));
        Assert.Equal(SoftInstrumentKind.Inventory, cat.TryResolve("inventory"));
        Assert.Equal(SoftInstrumentKind.Inventory, cat.TryResolve("cdp_inventory"));
        Assert.Equal(SoftInstrumentKind.Inventory, cat.TryResolve("gaps"));
        Assert.Equal(SoftInstrumentKind.VerifyWave, cat.TryResolve("verify_wave"));
        Assert.Equal(SoftInstrumentKind.VerifyWave, cat.TryResolve("cdp_verify_wave"));
        Assert.Null(cat.TryResolve("editor_scene"));
        Assert.Null(cat.TryResolve("git_scene"));
    }

    [Fact]
    public void SoftInstrumentBoardHit_feeds_presenter()
    {
        var hit = new SoftInstrumentBoardHit(new { ok = true }, "armed", "pressure_channel/v1");
        var meta = new SoftInstrumentBoardMetaCatalog.Meta(
            "pressure_desk", "cdp_pressure", SoftInstrumentPresentMode.PulseLine, "hint");
        dynamic pulse = SeatInstrumentPanePresenter.Present(
            meta, false, hit.Board, hit.Pulse, hit.Schema);
        Assert.Equal("armed", (string)pulse.pulse);
        Assert.Equal("pressure_channel/v1", (string)pulse.schema);
    }

    [Fact]
    public void SoftInstrumentBoardMetaCatalog_covers_all_kinds()
    {
        var cat = new SoftInstrumentBoardMetaCatalog();
        foreach (SoftInstrumentKind kind in Enum.GetValues<SoftInstrumentKind>())
        {
            var m = cat.Require(kind);
            Assert.False(string.IsNullOrWhiteSpace(m.Go));
            Assert.False(string.IsNullOrWhiteSpace(m.Tool));
        }
        Assert.Equal("cdp_search", cat.Require(SoftInstrumentKind.FindDesk).Tool);
        Assert.Equal("plan", cat.Require(SoftInstrumentKind.Plan).Go);
        Assert.Equal("arch_desk", cat.Require(SoftInstrumentKind.ArchDesk).Go);
        Assert.Equal("cdp_arch", cat.Require(SoftInstrumentKind.ArchDesk).Tool);
        Assert.Equal("refactor_plan", cat.Require(SoftInstrumentKind.RefactorPlan).Go);
        Assert.Equal("cdp_refactor", cat.Require(SoftInstrumentKind.RefactorPlan).Tool);
        Assert.Equal("ps1_scene", cat.Require(SoftInstrumentKind.Ps1Desk).Go);
        Assert.Equal("cdp_ps1_scene", cat.Require(SoftInstrumentKind.Ps1Desk).Tool);
        Assert.Equal("md_author", cat.Require(SoftInstrumentKind.MdAuthor).Go);
        Assert.Equal("cdp_md_author", cat.Require(SoftInstrumentKind.MdAuthor).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.MdAuthor).Mode);
        Assert.Equal("learn", cat.Require(SoftInstrumentKind.Learn).Go);
        Assert.Equal("cdp_learn", cat.Require(SoftInstrumentKind.Learn).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Learn).Mode);
        Assert.Equal("domain", cat.Require(SoftInstrumentKind.Domain).Go);
        Assert.Equal("cdp_domain", cat.Require(SoftInstrumentKind.Domain).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Domain).Mode);
        Assert.Equal("calendar", cat.Require(SoftInstrumentKind.Calendar).Go);
        Assert.Equal("cdp_calendar", cat.Require(SoftInstrumentKind.Calendar).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Calendar).Mode);
        Assert.Equal("rules", cat.Require(SoftInstrumentKind.Rules).Go);
        Assert.Equal("cdp_rules", cat.Require(SoftInstrumentKind.Rules).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Rules).Mode);
        Assert.Equal("inventory", cat.Require(SoftInstrumentKind.Inventory).Go);
        Assert.Equal("cdp_inventory", cat.Require(SoftInstrumentKind.Inventory).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Inventory).Mode);
        Assert.Equal("verify_wave", cat.Require(SoftInstrumentKind.VerifyWave).Go);
        Assert.Equal("cdp_verify_wave", cat.Require(SoftInstrumentKind.VerifyWave).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.VerifyWave).Mode);
        Assert.Equal("project_switch", cat.Require(SoftInstrumentKind.ProjectSwitch).Go);
        Assert.Equal("cdp_scope", cat.Require(SoftInstrumentKind.ProjectSwitch).Tool);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.ProjectSwitch).Mode);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.Ps1Desk).Mode);
        Assert.Equal(SoftInstrumentPresentMode.PulseLine, cat.Require(SoftInstrumentKind.PressureDesk).Mode);
        Assert.Equal(SoftInstrumentPresentMode.PulseWithResult, cat.Require(SoftInstrumentKind.Problems).Mode);
        Assert.Equal(SoftInstrumentPresentMode.FullOr, cat.Require(SoftInstrumentKind.Plan).Mode);
    }

    [Fact]
    public void SoftInstrumentBoardMeta_deferred_kinds_have_placeable_go()
    {
        var cat = new SoftInstrumentBoardMetaCatalog();
        Assert.Equal("problems", cat.Require(SoftInstrumentKind.Problems).Go);
        Assert.Equal("plugins", cat.Require(SoftInstrumentKind.Plugins).Go);
        Assert.Equal("review", cat.Require(SoftInstrumentKind.Review).Go);
        Assert.Equal("sys", cat.Require(SoftInstrumentKind.Sys).Go);
        Assert.Equal("ecl", cat.Require(SoftInstrumentKind.Ecl).Go);
        Assert.Equal("qrh", cat.Require(SoftInstrumentKind.Qrh).Go);
        Assert.Equal("alert", cat.Require(SoftInstrumentKind.Alert).Go);
    }

    [Fact]
    public void SoftInstrumentPresentMode_Quality_wantFull_matches_dispatch_envelope()
    {
        var meta = new SoftInstrumentBoardMetaCatalog().Require(SoftInstrumentKind.Quality);
        var board = new { ok = true, pulse = "gates ok" };
        dynamic full = SeatInstrumentPanePresenter.Present(meta, wantFull: true, board, "gates ok");
        Assert.Equal("quality", (string)full.go);
        Assert.Equal("quality_gates", (string)full.tool);
        Assert.Equal("full", (string)full.detail);
        Assert.NotNull(full.result);
    }

    [Fact]
    public void SeatInstrumentPanePresenter_Present_modes()
    {
        var board = new { ok = true };
        var fullOr = new SoftInstrumentBoardMetaCatalog.Meta("x", "t");
        dynamic asIs = SeatInstrumentPanePresenter.Present(fullOr, false, board);
        Assert.True((bool)asIs.ok);
        dynamic wrapped = SeatInstrumentPanePresenter.Present(fullOr, true, board);
        Assert.Equal("full", (string)wrapped.detail);

        var pulseLine = new SoftInstrumentBoardMetaCatalog.Meta(
            "pressure_desk", "cdp_pressure", SoftInstrumentPresentMode.PulseLine, "hint");
        dynamic pulse = SeatInstrumentPanePresenter.Present(pulseLine, false, board, "armed", "pressure_channel/v1");
        Assert.Equal("pulse", (string)pulse.detail);
        Assert.Equal("armed", (string)pulse.pulse);
        Assert.Equal("hint", (string)pulse.hint);

        var withResult = new SoftInstrumentBoardMetaCatalog.Meta(
            "problems", "problems_channel", SoftInstrumentPresentMode.PulseWithResult);
        dynamic pr = SeatInstrumentPanePresenter.Present(withResult, false, board, "E×1");
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
    public void SeatInstrumentPanePresenter_pulse_or_full()
    {
        var board = new { ok = true };
        dynamic pulse = SeatInstrumentPanePresenter.PulseOrFull(
            false, board, "x", "t", "line", "sch", "hint");
        Assert.Equal("pulse", (string)pulse.detail);
        Assert.Equal("line", (string)pulse.pulse);
        dynamic full = SeatInstrumentPanePresenter.PulseOrFull(
            true, board, "x", "t", "line", "sch", "hint");
        Assert.Equal("full", (string)full.detail);
    }

    [Fact]
    public void BuildAsync_peels_WorldGo_and_LegacyTiles_exist()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Ingress.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Nav.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.WorldGo.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.LegacyTiles.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Probes.cs")), root);
    }
}
