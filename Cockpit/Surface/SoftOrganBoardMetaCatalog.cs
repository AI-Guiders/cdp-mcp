#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: SoftOrganKind → go/tool wire labels (Handle still peel).</summary>
public sealed class SoftOrganBoardMetaCatalog : ICockpitComputeUnit
{
    public readonly record struct Meta(string Go, string Tool);

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
        SoftOrganKind.PressureDesk => new("pressure_desk", "cdp_pressure"),
        SoftOrganKind.OnboardDesk => new("onboard_desk", "cdp_onboard"),
        SoftOrganKind.Toolchain => new("toolchain", "cdp_toolchain"),
        SoftOrganKind.Alert => new("alert", "alert_channel"),
        SoftOrganKind.Problems => new("problems", "problems_channel"),
        SoftOrganKind.Plugins => new("plugins", "plugins_channel"),
        SoftOrganKind.Quality => new("quality", "quality_gates"),
        SoftOrganKind.Sys => new("sys", "sys_organ"),
        SoftOrganKind.Ecl => new("ecl", "ecl_organ"),
        SoftOrganKind.Qrh => new("qrh", "qrh_organ"),
        SoftOrganKind.Review => new("review", "review_organ"),
        _ => null
    };
}
