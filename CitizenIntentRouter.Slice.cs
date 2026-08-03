#nullable enable

namespace CdpMcp;
internal static partial class CitizenIntentRouter
{
    /// <summary>TM/plan CCL heads only — not shell/run/build (citizen host-execute gate).</summary>
    internal static bool IsPlanReplCmd(string cmd)
    {
        var head = cmd.Trim();
        if (head.Length == 0)
            return false;
        var sp = head.IndexOf(' ');
        if (sp > 0)
            head = head[..sp];
        return head.ToLowerInvariant() switch
        {
            "feature" or "intent" or "task" or "add" or "done" or "start" or "shipped" or "focus" or "park" or "defer" or "drop" or "note" or "events" or "product" or "phase" or "start_phase" or "complete_phase" or "criteria" or "criterion" or "leftover" or "share" or "promote" or "confirm" or "reject" or "await_operator" or "board" or "plan" => true,
            _ => false
        };
    }

    static bool LooksLikeWSpray(string raw)
    {
        if (raw.Contains("seats_detail=full", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Contains("ListTools", StringComparison.OrdinalIgnoreCase))
            return true;
        return raw.Contains("W-spray", StringComparison.OrdinalIgnoreCase) || raw.Contains("wspray", StringComparison.OrdinalIgnoreCase);
    }

    static string? MapDrillGo(string organ)
    {
        var o = organ.Trim().ToLowerInvariant();
        return o switch
        {
            "editor" or "forward" or "f" => "editor_scene",
            "plan" or "p" => "plan",
            "shell" or "m" => "shell_scene",
            "alert" or "sa" => "alert",
            "pressure" => "pressure",
            "calendar" or "clock" or "calendar_desk" => "calendar",
            "land" or "deep_link" or "deeplink" => "land",
            "pkg" or "nuget" or "packages" or "package" => "pkg",
            "project" or "projects" or "sln" or "solution" or "project_scene" => "project",
            "settings" or "options" or "prefs" or "ide_settings" or "tools_options" or "languages" => "settings",
            "restore" or "restore_previous" or "desk_restore" or "recent" or "open_recent" => "restore",
            "intercom" or "cide_intercom" or "intercom_send" or "intercom_scene" or "intercom_ack" or "intercom_history" or "intercom_presence" => "intercom",
            "cide_presentation" or "presentation" or "presentation_set" or "presentation_scene" => "cide_presentation",
            "toolchain" or "toolchain_desk" or "cdp_toolchain" or "toolchain_ensure" or "toolchain_probe" => "toolchain",
            "cockpit_host" or "cdp_cockpit_host" or "cockpit_start" or "cockpit_stop" => "cockpit_host",
            "qrh" or "eqrh" or "cdp_qrh" or "qrh_open" or "qrh_search" or "qrh_index" => "qrh",
            "webcam" or "webcam_desk" or "cdp_webcam" or "webcam_frame" or "webcam_scene" => "webcam_desk",
            "evidence" or "cdp_evidence" or "report" or "pfd" or "evidence_build" or "evidence_test" => "report",
            "domain" or "domain_desk" or "cdp_domain" or "domain_scene" or "domain_pulse" or "domain_list" or "domain_card" => "domain",
            "ps1" or "ise" or "ps1_scene" or "ps1_desk" or "cdp_ps1" or "cdp_ps1_scene" or "ps1_put" or "ps1_run" => "ps1_scene",
            "icm" or "icm_desk" or "cdp_icm" or "command_module" or "icm_aliases" or "icm_resolve" or "icm_invoke" => "icm_desk",
            "files" or "files_desk" or "cdp_files" or "file_manager" or "fm" or "files_list" or "files_tree" or "files_open" => "files_desk",
            "onboard" or "onboard_desk" or "explore_desk" or "explore" or "cdp_onboard" or "onboard_scan" or "onboard_clear" => "onboard_desk",
            "peel" or "peel_desk" or "cdp_peel" or "peel_preview" or "peel_apply" => "peel",
            "edit_plan" or "edit_plan_desk" or "cdp_edit_plan" or "edit_plan_draft" or "edit_plan_validate" or "edit_plan_apply" or "edit_plan_preview" => "edit_plan",
            "analysis" or "analysis_desk" or "analysis_scene" or "cdp_analysis" or "cdp_analysis_scene" or "analysis_map" or "analysis_clones" or "analysis_correspondence" or "analysis_corr" or "analysis_semantic" or "analysis_semantic_map" => "analysis_scene",
            "test_plan" or "test_plan_desk" or "cdp_test_plan" or "test_plan_preview" or "test_plan_apply" or "test_plan_draft" or "test_plan_run" => "test_plan",
            "test_scene" or "test_scene_desk" or "cdp_test_scene" or "test_runner" => "test_scene",
            "editor_scene" or "editor_scene_desk" or "cdp_editor_scene" or "editor_desk" => "editor_scene",
            "man" or "man_desk" or "cdp_man" or "manual" => "man",
            "health" or "health_desk" or "cdp_health" or "ops_health" => "health",
            "context" or "context_desk" or "cdp_context" or "session_context" => "context",
            "quality" or "quality_desk" or "quality_gates" or "cdp_quality" or "gates" or "quality_disk" or "quality_assert" or "quality_adx" => "quality",
            "session" or "session_desk" or "session_plane" or "cdp_session" => "session",
            "tools" or "tools_desk" or "tools_palette" or "cdp_tools" or "palette" => "tools",
            "capabilities" or "capabilities_desk" or "cdp_capabilities" or "caps" => "capabilities",
            "cockpit" or "cockpit_desk" or "cdp_cockpit" or "agent_desk" => "cockpit",
            "work_desk" or "cdp_work" or "intent_workspace" => "intent_workspace",
            "sa_desk" or "cdp_sa" or "code_sa" or "pre_sa" or "sa_code" => "sa_desk",
            "learn_desk" or "cdp_learn" or "learning" => "learn",
            "refactor_plan" or "cdp_refactor" or "debt_scene" => "refactor_plan",
            "goto" or "goto_all" or "go_to_all" or "cdp_goto" or "goto_feature" or "goto_desk" or "go_to" => "goto",
            "find" or "search" or "find_desk" => "find_desk",
            _ => o
        };
    }
}