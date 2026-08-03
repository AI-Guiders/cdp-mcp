#nullable enable

namespace CdpMcp;

/// <summary>
/// Efferent peel #10: map parsed <c>@intent</c> lines to organ routes (go=/drill/open/cmd).
/// Does not CallTool — host executes <see cref="Route"/>. Refuse W-spray as thrash string.
/// </summary>
internal static partial class CitizenIntentRouter
{
    public enum Verb
    {
        Go,
        Drill,
        PaneFull,
        Open,
        Replace,
        Create,
        Append,
        Delete,
        Kb,
        Build,
        Test,
        Run,
        Mcp,
        Shell,
        Debug,
        Git,
        Find,
        Ide,
        Ignite,
        Pressure,
        Browser,
        Script,
        Calendar,
        Land,
        Pkg,
        Project,
        Settings,
        Restore,
        Intercom,
        Presentation,
        Toolchain,
        CockpitHost,
        Qrh,
        Webcam,
        Evidence,
        Domain,
        Ps1,
        Icm,
        Files,
        Onboard,
        Peel,
        EditPlan,
        Analysis,
        TestPlan,
        TestScene,
        GotoAll,
        EditorScene,
        Man,
        Health,
        Quality,
        Session,
        Tools,
        Capabilities,
        Cockpit,
        Work,
        Sa,
        Learn,
        Refactor,
        Context,
        Edit,
        Deploy,
        Undo,
        Clip,
        ReplaceAll,
        Nav,
        Put,
        Scratch,
        Take,
        Share,
        Disk,
        Sniper,
        Buffer,
        FindBuf,
        Detail,
        Cmd,
        Refuse,
        Unknown
    }

    public sealed record Route(
        Verb Verb,
        string Raw,
        bool Ok,
        string? Go = null,
        string? Organ = null,
        string? Path = null,
        string? Detail = null,
        string? Scene = null,
        string? Cmd = null,
        string? OldString = null,
        string? NewString = null,
        string? Op = null,
        string? Server = null,
        string? Tool = null,
        string? Preset = null,
        string? Command = null,
        string? Reason = null);

    public static IReadOnlyList<Route> RouteAll(IEnumerable<CitizenWireParser.Message>? messages)
    {
        if (messages is null)
            return [];

        var list = new List<Route>();
        foreach (var m in messages)
        {
            if (m.Kind != CitizenWireParser.Kind.Intent)
                continue;
            list.Add(RouteOne(m.IntentText ?? ""));
        }

        return list;
    }

    public static Route RouteOne(string? intentText)
    {
        var raw = (intentText ?? "").Trim();
        if (raw.Length == 0)
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "empty_intent");

        if (LooksLikeWSpray(raw))
        {
            return new Route(
                Verb.Refuse,
                raw,
                Ok: false,
                Reason: "refuse_w_spray — seats_detail=full / catalog dump is thrash");
        }

        return TryRouteCore(raw)
            ?? TryRouteDoc(raw)
            ?? TryRouteDesk(raw)
            ?? TryRouteRuntime(raw)
            ?? TryRouteOrgansA(raw)
            ?? TryRouteOrgansB(raw)
            ?? TryRouteNav(raw)
            ?? TryRouteDetailOrUnknown(raw);
    }

    static Route TryRouteDetailOrUnknown(string raw)
    {
        if (TryKv(raw, out var detail, out var scene)
            && (!string.IsNullOrEmpty(detail) || !string.IsNullOrEmpty(scene)))
        {
            return new Route(
                Verb.Detail,
                raw,
                Ok: true,
                Detail: detail,
                Scene: scene,
                Go: scene);
        }

        return new Route(Verb.Unknown, raw, Ok: false, Reason: "unrecognized_intent");
    }


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
            "feature" or "intent" or "task" or "add"
                or "done" or "start" or "shipped" or "focus"
                or "park" or "defer" or "drop" or "note" or "events"
                or "product" or "phase" or "start_phase" or "complete_phase"
                or "criteria" or "criterion" or "leftover"
                or "share" or "promote" or "confirm" or "reject"
                or "await_operator" or "board" or "plan"
                => true,
            _ => false
        };
    }

    static bool LooksLikeWSpray(string raw)
    {
        if (raw.Contains("seats_detail=full", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Contains("ListTools", StringComparison.OrdinalIgnoreCase))
            return true;
        return raw.Contains("W-spray", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("wspray", StringComparison.OrdinalIgnoreCase);
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
            "quality" or "quality_desk" or "quality_gates" or "cdp_quality" or "gates"
                or "quality_disk" or "quality_assert" or "quality_adx" => "quality",
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

    static string? ExtractPath(string raw)
    {
        const string pathEq = "path=";
        var idx = raw.IndexOf(pathEq, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return raw[(idx + pathEq.Length)..].Trim().Trim('"');

        if (raw.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
            return raw["open ".Length..].Trim().Trim('"');

        return null;
    }

static Route RouteMcp(string raw)
    {
        var op = ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(op) && raw.StartsWith("mcp ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["mcp ".Length..].Trim();
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (IsMcpOp(head))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        if (op is "status" or "list")
            op = "scene";
        else if (op is "invoke")
            op = "call";
        else if (op is "list_tools")
            op = "tools";
        else if (op is "connect" or "add")
            op = "mount";
        else if (op is "catalog")
            op = "presets";

        if (!IsMcpOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "mcp_op_unknown");

        var server = ExtractKeyedValue(raw, "server") ?? ExtractKeyedValue(raw, "id");
        var tool = ExtractKeyedValue(raw, "tool") ?? ExtractKeyedValue(raw, "name");
        var preset = ExtractKeyedValue(raw, "preset");

        if (op is "call" && string.IsNullOrWhiteSpace(tool))
            return new Route(Verb.Mcp, raw, Ok: false, Op: op, Server: server, Tool: tool, Preset: preset, Go: "mcp", Reason: "mcp_tool_required");
        if ((op is "call" or "tools" or "unmount") && string.IsNullOrWhiteSpace(server))
            return new Route(Verb.Mcp, raw, Ok: false, Op: op, Server: server, Tool: tool, Preset: preset, Go: "mcp", Reason: "mcp_server_required");
        if (op is "mount" && string.IsNullOrWhiteSpace(preset) && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "command")))
            return new Route(Verb.Mcp, raw, Ok: false, Op: op, Server: server, Tool: tool, Preset: preset, Go: "mcp", Reason: "mcp_preset_or_command_required");

        return new Route(
            Verb.Mcp,
            raw,
            Ok: true,
            Op: op,
            Server: server,
            Tool: tool,
            Preset: preset,
            Go: "mcp");
    }

    static bool IsMcpOp(string? op) =>
        op is "scene" or "status" or "list" or "presets" or "catalog"
            or "mount" or "connect" or "add"
            or "tools" or "list_tools"
            or "call" or "invoke"
            or "unmount";

    static string? ExtractLifecyclePath(string raw, string verb)
    {
        if (raw.Equals(verb, StringComparison.OrdinalIgnoreCase))
            return null;

        // Prefer keyed path= (supports quotes / spaces); stop before filter= via ExtractKeyedValue.
        if (ExtractKeyedValue(raw, "path") is { Length: > 0 } keyed)
            return keyed.Trim();

        var prefix = verb + " ";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw[prefix.Length..].Trim();
            if (rest.StartsWith("filter=", StringComparison.OrdinalIgnoreCase))
                return null;
            if (rest.StartsWith('"'))
            {
                var end = rest.IndexOf('"', 1);
                if (end > 0)
                    return rest[1..end];
            }

            var space = rest.IndexOf(' ');
            if (space > 0)
                rest = rest[..space];
            return rest.Length == 0 ? null : rest.Trim().Trim('"');
        }

        return null;
    }

    /// <summary>
    /// <c>replace path=… old="…" new="…"</c> — quoted old/new (spaces ok); path unquoted token.
    /// </summary>
    static bool TryParseReplace(
        string raw,
        out string? path,
        out string? oldString,
        out string? newString,
        out string? reason)
    {
        path = null;
        oldString = null;
        newString = null;
        reason = null;

        path = ExtractKeyedValue(raw, "path");
        oldString = ExtractKeyedValue(raw, "old") ?? ExtractKeyedValue(raw, "old_string");
        newString = ExtractKeyedValue(raw, "new") ?? ExtractKeyedValue(raw, "new_string");

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "replace_path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(oldString))
        {
            reason = "replace_old_empty";
            return false;
        }

        newString ??= "";
        return true;
    }

    internal static string? ExtractKeyedValue(string raw, string key)
    {
        var needle = key + "=";
        var idx = raw.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var i = idx + needle.Length;
        if (i >= raw.Length)
            return "";

        if (raw[i] == '"')
        {
            var end = raw.IndexOf('"', i + 1);
            if (end < 0)
                return raw[(i + 1)..];
            return raw[(i + 1)..end];
        }

        var rest = raw[i..];
        var sp = rest.IndexOf(' ');
        return sp < 0 ? rest : rest[..sp];
    }

    static bool TryKv(string raw, out string? detail, out string? scene)
    {
        detail = null;
        scene = null;
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = p[..eq];
            var val = p[(eq + 1)..];
            if (key.Equals("detail", StringComparison.OrdinalIgnoreCase))
                detail = val;
            else if (key.Equals("scene", StringComparison.OrdinalIgnoreCase))
                scene = val;
        }

        return detail is not null || scene is not null;
    }
}
