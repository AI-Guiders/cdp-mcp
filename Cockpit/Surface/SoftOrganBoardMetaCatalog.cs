#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: SoftOrganKind → go/tool/present mode (boards via ISoftOrganBoard).</summary>
public sealed class SoftOrganBoardMetaCatalog : ICockpitComputeUnit
{
    public readonly record struct Meta(
        string Go,
        string Tool,
        SoftOrganPresentMode Mode = SoftOrganPresentMode.FullOr,
        string? PulseHint = null);

    public Meta Require(SoftOrganKind kind) =>
        TryGet(kind) ?? throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

    public Meta? TryGet(SoftOrganKind kind) => kind switch
    {
        SoftOrganKind.Plan => new("plan", "cdp_work"),
        SoftOrganKind.Report => new("report", "report_board"),
        SoftOrganKind.FindDesk => new("find_desk", "cdp_search"),
        SoftOrganKind.SaDesk => new("sa_desk", "cdp_sa"),
        SoftOrganKind.DebugDesk => new("debug_desk", "cdp_debug_sa"),
        SoftOrganKind.TestDesk => new("test_desk", "cdp_test_sa"),
        SoftOrganKind.BuildDesk => new("build_desk", "cdp_build_sa"),
        SoftOrganKind.Crm => new("crm", "cdp_crm"),
        SoftOrganKind.FilesDesk => new("files_desk", "cdp_files"),
        SoftOrganKind.IgniteDesk => new("ignite_desk", "cdp_ignite"),
        SoftOrganKind.WebcamDesk => new("webcam_desk", "cdp_webcam"),
        SoftOrganKind.PressureDesk => new(
            "pressure_desk", "cdp_pressure", SoftOrganPresentMode.PulseLine,
            "pane_full= / go_detail=full for checklist dump"),
        SoftOrganKind.OnboardDesk => new(
            "onboard_desk", "cdp_onboard", SoftOrganPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=scan to refresh"),
        SoftOrganKind.Toolchain => new(
            "toolchain", "cdp_toolchain", SoftOrganPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=ensure id=python|gcc|…"),
        SoftOrganKind.Alert => new("alert", "alert_channel"),
        SoftOrganKind.Problems => new("problems", "problems_channel", SoftOrganPresentMode.PulseWithResult),
        SoftOrganKind.Plugins => new("plugins", "plugins_channel", SoftOrganPresentMode.PulseWithResult),
        SoftOrganKind.Quality => new("quality", "quality_gates", SoftOrganPresentMode.PulseWithResult),
        SoftOrganKind.ArchDesk => new("arch_desk", "cdp_arch"),
        SoftOrganKind.RefactorPlan => new("refactor_plan", "cdp_refactor"),
        SoftOrganKind.Ps1Desk => new(
            "ps1_scene", "cdp_ps1_scene", SoftOrganPresentMode.PulseLine,
            "pane_full= / go_detail=full · put→AST check→pwsh -File"),
        SoftOrganKind.Sys => new("sys", "sys_organ"),
        SoftOrganKind.Ecl => new("ecl", "ecl_organ"),
        SoftOrganKind.Qrh => new("qrh", "qrh_organ"),
        SoftOrganKind.Review => new("review", "review_organ"),
        SoftOrganKind.MdAuthor => new(
            "md_author", "cdp_md_author", SoftOrganPresentMode.PulseLine,
            "pane_full= / go_detail=full · op=check|expand|export path="),
        _ => null
    };
}
