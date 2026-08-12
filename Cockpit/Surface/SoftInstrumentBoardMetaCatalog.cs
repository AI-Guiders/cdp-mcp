#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: SoftInstrumentKind → go/tool/present mode (boards via ISoftInstrumentBoard).</summary>
public sealed class SoftInstrumentBoardMetaCatalog : ICockpitComputeUnit
{
    public readonly record struct Meta(
        string Go,
        string Tool,
        SoftInstrumentPresentMode Mode = SoftInstrumentPresentMode.FullOr,
        string? PulseHint = null);

    public Meta Require(SoftInstrumentKind kind) =>
        TryGet(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    public Meta? TryGet(SoftInstrumentKind kind) => kind switch
    {
        SoftInstrumentKind.Plan => new("plan", "cdp_work"),
        SoftInstrumentKind.Report => new("report", "report_board"),
        SoftInstrumentKind.FindDesk => new("find_desk", "cdp_search"),
        SoftInstrumentKind.SaDesk => new("sa_desk", "cdp_sa"),
        SoftInstrumentKind.DebugDesk => new("debug_desk", "cdp_debug_sa"),
        SoftInstrumentKind.TestDesk => new("test_desk", "cdp_test_sa"),
        SoftInstrumentKind.BuildDesk => new("build_desk", "cdp_build_sa"),
        SoftInstrumentKind.Crm => new("crm", "cdp_crm"),
        SoftInstrumentKind.FilesDesk => new("files_desk", "cdp_files"),
        SoftInstrumentKind.IgniteDesk => new("ignite_desk", "cdp_ignite"),
        SoftInstrumentKind.WebcamDesk => new("webcam_desk", "cdp_webcam"),
        SoftInstrumentKind.PressureDesk => new(
            "pressure_desk", "cdp_pressure", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full for checklist dump"),
        SoftInstrumentKind.OnboardDesk => new(
            "onboard_desk", "cdp_onboard", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=scan to refresh"),
        SoftInstrumentKind.Toolchain => new(
            "toolchain", "cdp_toolchain", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=ensure id=python|gcc|…"),
        SoftInstrumentKind.Alert => new("alert", "alert_channel"),
        SoftInstrumentKind.Problems => new("problems", "problems_channel", SoftInstrumentPresentMode.PulseWithResult),
        SoftInstrumentKind.Plugins => new("plugins", "plugins_channel", SoftInstrumentPresentMode.PulseWithResult),
        SoftInstrumentKind.Quality => new("quality", "quality_gates", SoftInstrumentPresentMode.PulseWithResult),
        SoftInstrumentKind.ArchDesk => new("arch_desk", "cdp_arch"),
        SoftInstrumentKind.RefactorPlan => new("refactor_plan", "cdp_refactor"),
        SoftInstrumentKind.Ps1Desk => new(
            "ps1_scene", "cdp_ps1_scene", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · put→AST check→pwsh -File"),
        SoftInstrumentKind.Sys => new("sys", "sys_organ"),
        SoftInstrumentKind.Ecl => new("ecl", "ecl_organ"),
        SoftInstrumentKind.Qrh => new("qrh", "qrh_organ"),
        SoftInstrumentKind.Review => new("review", "review_organ"),
        SoftInstrumentKind.MdAuthor => new(
            "md_author", "cdp_md_author", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=check|expand|export path="),
        SoftInstrumentKind.Learn => new(
            "learn", "cdp_learn", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=stash|list|recall|promote"),
        SoftInstrumentKind.ProjectSwitch => new(
            "project_switch", "cdp_scope", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=set primary= scope= · go=ps (not go=scope sniper)"),
        SoftInstrumentKind.Domain => new(
            "domain", "cdp_domain", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=pulse|list|card id="),
        SoftInstrumentKind.Calendar => new(
            "calendar", "cdp_calendar", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=pulse|month"),
        SoftInstrumentKind.Rules => new(
            "rules", "cdp_rules", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=pulse|list|card id=healthy-agent"),
        SoftInstrumentKind.Inventory => new(
            "inventory", "cdp_inventory", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=scene|pulse — throughput gaps [A]"),
        SoftInstrumentKind.VerifyWave => new(
            "verify_wave", "cdp_verify_wave", SoftInstrumentPresentMode.PulseLine,
            "pane_full= / go_detail=full · ship checklist (no in-proc KillRunning)"),
        _ => null
    };
}
