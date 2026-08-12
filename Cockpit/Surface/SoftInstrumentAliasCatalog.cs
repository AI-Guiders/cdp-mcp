#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: planPin / go aliases → SoftInstrumentKind (seats + SoftDispatch).</summary>
public sealed class SoftInstrumentAliasCatalog : ICockpitComputeUnit
{
    public SoftInstrumentKind? TryResolve(string? planPin) => planPin switch
    {
        "plan" or "work" or "tasks" or "tm" or "task" or "feature"
            or "promote" or "confirm" or "reject" or "phase" => SoftInstrumentKind.Plan,
        "report" or "evidence" or "pfd" => SoftInstrumentKind.Report,
        "find_desk" or "search_desk" or "code_search" or "cdp_search" => SoftInstrumentKind.FindDesk,
        "sa_desk" or "code_sa" or "pre_sa" or "sa_code" or "cdp_sa" => SoftInstrumentKind.SaDesk,
        "debug_desk" or "dap_sa" or "debug_sa" or "cdp_debug_sa" => SoftInstrumentKind.DebugDesk,
        "test_desk" or "test_sa" or "cdp_test_sa" => SoftInstrumentKind.TestDesk,
        "build_desk" or "ship_desk" or "build_sa" or "ship_sa" or "cdp_build_sa" => SoftInstrumentKind.BuildDesk,
        "crm" or "callout" or "crm_panel" or "cdp_crm" => SoftInstrumentKind.Crm,
        "files_desk" or "files" or "explorer" or "fm" or "file_manager" or "cdp_files" => SoftInstrumentKind.FilesDesk,
        "ignite_desk" or "ignite" or "autoignite" or "cdt_ignite" or "cdp_ignite" => SoftInstrumentKind.IgniteDesk,
        "webcam_desk" or "webcam" or "camera" or "sense" or "cdp_webcam" => SoftInstrumentKind.WebcamDesk,
        "pressure_desk" or "pressure" or "compact_prep" or "pre_compact" or "cdp_pressure" => SoftInstrumentKind.PressureDesk,
        "onboard_desk" or "explore_desk" or "onboard" or "explore" or "cdp_onboard" => SoftInstrumentKind.OnboardDesk,
        "toolchain" or "toolchain_desk" or "cdp_toolchain"
            or "toolchain_ensure" or "toolchain_probe"
            or "toolchain_install" or "toolchain_add" => SoftInstrumentKind.Toolchain,
        "alert" or "eicas" or "sa" => SoftInstrumentKind.Alert,
        "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags" => SoftInstrumentKind.Problems,
        "plugins" or "plugin" or "vsix" => SoftInstrumentKind.Plugins,
        "quality" or "gates" => SoftInstrumentKind.Quality,
        "arch_desk" or "arch_board" or "board" or "sketch_desk" or "cdp_arch" => SoftInstrumentKind.ArchDesk,
        "refactor_plan" or "refactor" or "cdp_refactor" or "debt_scene" => SoftInstrumentKind.RefactorPlan,
        "ps1_scene" or "ps1" or "ise" or "ps1_desk" or "cdp_ps1_scene" => SoftInstrumentKind.Ps1Desk,
        "sys" => SoftInstrumentKind.Sys,
        "ecl" or "chk" => SoftInstrumentKind.Ecl,
        "qrh" or "eqrh" or "handbook" => SoftInstrumentKind.Qrh,
        "review" => SoftInstrumentKind.Review,
        "md_author" or "md_author_desk" or "markdown_author" or "md_include" or "cdp_md_author"
            => SoftInstrumentKind.MdAuthor,
        "learn" or "learn_desk" or "learning" or "cdp_learn"
            => SoftInstrumentKind.Learn,
        "project_switch" or "ps" or "primary_scope" or "scope_desk" or "cdp_scope"
            => SoftInstrumentKind.ProjectSwitch,
        "domain" or "domain_desk" or "ownership" or "cdp_domain"
            => SoftInstrumentKind.Domain,
        "calendar" or "calendar_desk" or "clock" or "local_clock" or "cdp_calendar"
            => SoftInstrumentKind.Calendar,
        "rules" or "rules_desk" or "standing" or "healthy_agent" or "cdp_rules"
            => SoftInstrumentKind.Rules,
        "inventory" or "inventory_desk" or "gaps" or "cdp_inventory"
            => SoftInstrumentKind.Inventory,
        "verify_wave" or "verify_wave_desk" or "wave_verify" or "cdp_verify_wave"
            => SoftInstrumentKind.VerifyWave,
        _ => null
    };
}
