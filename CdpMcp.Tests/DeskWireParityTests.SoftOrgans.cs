#nullable enable
using CdpMcp.Cockpit.Surface;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Soft-organ / seat-presenter / Build peel file asserts (DeskWireParity).</summary>
public sealed partial class DeskWireParityTests
{
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
        Assert.Equal(SoftOrganKind.ArchDesk, cat.TryResolve("arch_desk"));
        Assert.Equal(SoftOrganKind.ArchDesk, cat.TryResolve("board"));
        Assert.Equal(SoftOrganKind.ArchDesk, cat.TryResolve("cdp_arch"));
        Assert.Equal(SoftOrganKind.RefactorPlan, cat.TryResolve("refactor_plan"));
        Assert.Equal(SoftOrganKind.RefactorPlan, cat.TryResolve("debt_scene"));
        Assert.Null(cat.TryResolve("editor_scene"));
        Assert.Null(cat.TryResolve("git_scene"));
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
        Assert.Equal("arch_desk", cat.Require(SoftOrganKind.ArchDesk).Go);
        Assert.Equal("cdp_arch", cat.Require(SoftOrganKind.ArchDesk).Tool);
        Assert.Equal("refactor_plan", cat.Require(SoftOrganKind.RefactorPlan).Go);
        Assert.Equal("cdp_refactor", cat.Require(SoftOrganKind.RefactorPlan).Tool);
        Assert.Equal(SoftOrganPresentMode.PulseLine, cat.Require(SoftOrganKind.PressureDesk).Mode);
        Assert.Equal(SoftOrganPresentMode.PulseWithResult, cat.Require(SoftOrganKind.Problems).Mode);
        Assert.Equal(SoftOrganPresentMode.FullOr, cat.Require(SoftOrganKind.Plan).Mode);
    }

    [Fact]
    public void SoftOrganBoardMeta_deferred_kinds_have_placeable_go()
    {
        var cat = new SoftOrganBoardMetaCatalog();
        Assert.Equal("problems", cat.Require(SoftOrganKind.Problems).Go);
        Assert.Equal("plugins", cat.Require(SoftOrganKind.Plugins).Go);
        Assert.Equal("review", cat.Require(SoftOrganKind.Review).Go);
        Assert.Equal("sys", cat.Require(SoftOrganKind.Sys).Go);
        Assert.Equal("ecl", cat.Require(SoftOrganKind.Ecl).Go);
        Assert.Equal("qrh", cat.Require(SoftOrganKind.Qrh).Go);
        Assert.Equal("alert", cat.Require(SoftOrganKind.Alert).Go);
    }

    [Fact]
    public void SoftOrganPresentMode_Quality_wantFull_matches_dispatch_envelope()
    {
        var meta = new SoftOrganBoardMetaCatalog().Require(SoftOrganKind.Quality);
        var board = new { ok = true, pulse = "gates ok" };
        dynamic full = SeatOrganPanePresenter.Present(meta, wantFull: true, board, "gates ok");
        Assert.Equal("quality", (string)full.go);
        Assert.Equal("quality_gates", (string)full.tool);
        Assert.Equal("full", (string)full.detail);
        Assert.NotNull(full.result);
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
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Ingress.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Nav.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.WorldGo.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.LegacyTiles.cs")), root);
        Assert.True(File.Exists(Path.Combine(root, "IdeCockpit.Build.Probes.cs")), root);
    }
}
