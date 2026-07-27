#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: planPin / go aliases → SoftOrganKind (seats + SoftDispatch).</summary>
public sealed class SoftOrganAliasCatalog : ICockpitComputeUnit
{
    public SoftOrganKind? TryResolve(string? planPin) => planPin switch
    {
        "plan" or "work" or "tasks" or "tm" or "task" or "feature"
            or "promote" or "confirm" or "reject" or "phase" => SoftOrganKind.Plan,
        "report" or "evidence" or "pfd" => SoftOrganKind.Report,
        "find_desk" or "search_desk" or "code_search" or "cdp_search" => SoftOrganKind.FindDesk,
        "sa_desk" or "code_sa" or "pre_sa" or "sa_code" or "cdp_sa" => SoftOrganKind.SaDesk,
        "debug_desk" or "dap_sa" or "debug_sa" or "cdp_debug_sa" => SoftOrganKind.DebugDesk,
        "test_desk" or "test_sa" or "cdp_test_sa" => SoftOrganKind.TestDesk,
        "build_desk" or "ship_desk" or "build_sa" or "ship_sa" or "cdp_build_sa" => SoftOrganKind.BuildDesk,
        "crm" or "callout" or "crm_panel" or "cdp_crm" => SoftOrganKind.Crm,
        "files_desk" or "files" or "explorer" or "fm" or "file_manager" or "cdp_files" => SoftOrganKind.FilesDesk,
        "ignite_desk" or "ignite" or "autoignite" or "cdt_ignite" or "cdp_ignite" => SoftOrganKind.IgniteDesk,
        "webcam_desk" or "webcam" or "camera" or "sense" or "cdp_webcam" => SoftOrganKind.WebcamDesk,
        "pressure_desk" or "pressure" or "compact_prep" or "pre_compact" or "cdp_pressure" => SoftOrganKind.PressureDesk,
        "onboard_desk" or "explore_desk" or "onboard" or "explore" or "cdp_onboard" => SoftOrganKind.OnboardDesk,
        "toolchain" or "toolchain_desk" or "cdp_toolchain"
            or "toolchain_ensure" or "toolchain_probe"
            or "toolchain_install" or "toolchain_add" => SoftOrganKind.Toolchain,
        "alert" or "eicas" or "sa" => SoftOrganKind.Alert,
        "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags" => SoftOrganKind.Problems,
        "plugins" or "plugin" or "vsix" => SoftOrganKind.Plugins,
        "quality" or "gates" => SoftOrganKind.Quality,
        "sys" => SoftOrganKind.Sys,
        "ecl" or "chk" => SoftOrganKind.Ecl,
        "qrh" or "eqrh" or "handbook" => SoftOrganKind.Qrh,
        "review" => SoftOrganKind.Review,
        _ => null
    };
}
