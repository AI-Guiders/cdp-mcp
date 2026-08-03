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

        if (raw.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = raw.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase)
                ? raw["cmd=".Length..].Trim()
                : raw["cmd ".Length..].Trim();
            cmd = cmd.Trim().Trim('"');
            if (cmd.Length == 0)
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "cmd_empty");
            if (!IsPlanReplCmd(cmd))
            {
                return new Route(
                    Verb.Refuse,
                    raw,
                    Ok: false,
                    Cmd: cmd,
                    Reason: "refuse_non_plan_repl — host cmd= is TM/CCL board only (feature|task|done|…)");
            }

            return new Route(Verb.Cmd, raw, Ok: true, Cmd: cmd, Go: "plan");
        }

        if (raw.StartsWith("go=", StringComparison.OrdinalIgnoreCase))
        {
            var go = raw["go=".Length..].Trim();
            return go.Length == 0
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "go_empty")
                : new Route(Verb.Go, raw, Ok: true, Go: go);
        }

        if (raw.StartsWith("drill ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("drill", StringComparison.OrdinalIgnoreCase))
        {
            var organ = raw.Equals("drill", StringComparison.OrdinalIgnoreCase)
                ? ""
                : raw["drill ".Length..].Trim();
            if (organ.Length == 0)
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "drill_organ_empty");
            var go = MapDrillGo(organ);
            return new Route(Verb.Drill, raw, Ok: true, Organ: organ, Go: go);
        }

        if (raw.StartsWith("pane_full=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pane_full ", StringComparison.OrdinalIgnoreCase))
        {
            var seat = raw.StartsWith("pane_full=", StringComparison.OrdinalIgnoreCase)
                ? raw["pane_full=".Length..].Trim()
                : raw["pane_full ".Length..].Trim();
            return seat.Length == 0
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "pane_full_empty")
                : new Route(Verb.PaneFull, raw, Ok: true, Organ: seat, Go: "cockpit");
        }

        if (raw.Equals("open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("open ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("open path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractPath(raw);
            return string.IsNullOrWhiteSpace(path)
                ? new Route(Verb.Unknown, raw, Ok: false, Reason: "open_path_empty")
                : new Route(Verb.Open, raw, Ok: true, Path: path, Go: "buffer");
        }

        if (raw.Equals("replace_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace_all ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace_all path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("replaceall", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replaceall ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteReplaceAll(raw);
        }

        if (raw.Equals("put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("put ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("put path=", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePut(raw);
        }

        if (raw.Equals("scratch", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scratch ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteScratch(raw);
        }

        if (raw.Equals("take", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("take ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("take path=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTake(raw);
        }

        if (raw.Equals("share", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share with=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("share from=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShare(raw);
        }

        if (raw.Equals("scope", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope from=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("scope_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sniper", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sniper ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek wire=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek pad=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("aim", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("aim ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("aim wire=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("target", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("target ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("outline", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("outline ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteSniper(raw);
        }

        if (raw.Equals("reload", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("reload ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("reload path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("keep_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("keep_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("keep_disk path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("disk_peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("disk_peek ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("disk_peek path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("diskpeek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("diskpeek ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peek_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peek_disk ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDisk(raw);
        }

        if (raw.Equals("read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("read ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("read path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("close ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("close path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffers", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffers ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("doc_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_scene", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffer_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_scene", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("buffer", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_diags", StringComparison.OrdinalIgnoreCase))
        {
            return RouteBuffer(raw);
        }

        if (raw.StartsWith("replace ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("replace path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseReplace(raw, out var path, out var oldString, out var newString, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Replace,
                raw,
                Ok: true,
                Path: path,
                OldString: oldString,
                NewString: newString,
                Go: "buffer");
        }

        if (raw.StartsWith("create ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("create path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("write ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("write path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCreate(raw, out var path, out var body, out var overwrite, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Create,
                raw,
                Ok: true,
                Path: path,
                NewString: body,
                Op: overwrite ? "overwrite" : null,
                Go: "buffer");
        }

        if (raw.StartsWith("append ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("append path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseAppend(raw, out var path, out var body, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Append,
                raw,
                Ok: true,
                Path: path,
                NewString: body,
                Go: "buffer");
        }

        if (raw.StartsWith("delete ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("delete path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rm ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rm path=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("remove ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("remove path=", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseDelete(raw, out var path, out var force, out var reason))
                return new Route(Verb.Unknown, raw, Ok: false, Reason: reason);
            return new Route(
                Verb.Delete,
                raw,
                Ok: true,
                Path: path,
                Op: force ? "force" : null,
                Go: "buffer");
        }

        if (raw.Equals("build", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("build ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("build path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(raw, "build");
            return new Route(Verb.Build, raw, Ok: true, Path: path, Go: "build");
        }

        if (raw.Equals("test_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_apply ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_draft", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_draft ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan_apply ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTestPlan(raw);
        }

        if (raw.Equals("test_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_scene_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_scene_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_runner", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_runner ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTestScene(raw);
        }

        if (raw.Equals("editor_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor_scene_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_scene_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_editor_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_editor_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteEditorScene(raw);
        }

        if (raw.Equals("man", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("man ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("man_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("man_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_man", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_man ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("manual", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("manual ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteMan(raw);
        }

        if (raw.Equals("health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("health ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("health_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("health_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_health ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ops_health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ops_health ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteHealth(raw);
        }

        if (raw.Equals("context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("context_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("context_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_context ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteContext(raw);
        }

        if (raw.Equals("quality", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_gates", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_gates ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_quality", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_quality ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_assert", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_assert ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_assertions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_assertions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_adx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_adx ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_project", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_project ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates_assert", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates_assert ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteQuality(raw);
        }

        if (raw.Equals("session", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_plane", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_plane ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_session", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_session ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteSession(raw);
        }

        if (raw.Equals("tools", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tools_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tools_palette", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools_palette ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_tools", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_tools ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("palette", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("palette ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTools(raw);
        }

        if (raw.Equals("capabilities", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("capabilities ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("capabilities_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("capabilities_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_capabilities", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_capabilities ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("caps", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("caps ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCapabilities(raw);
        }

        if (raw.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_cockpit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_cockpit ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("agent_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("agent_desk ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCockpit(raw);
        }

        if (raw.Equals("work", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("work ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("work_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("work_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_work", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_work ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intent_workspace", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intent_workspace ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteWork(raw);
        }

        if (raw.Equals("test", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test path=", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(raw, "test");
            return new Route(Verb.Test, raw, Ok: true, Path: path, Go: "test");
        }

        if (raw.Equals("run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("run ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("run path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("dotnet_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("dotnet_run ", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractLifecyclePath(
                raw.StartsWith("dotnet_run", StringComparison.OrdinalIgnoreCase)
                    ? "run" + raw["dotnet_run".Length..]
                    : raw,
                "run");
            return new Route(Verb.Run, raw, Ok: true, Path: path, Go: "run");
        }

        if (raw.Equals("mcp", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("mcp ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteMcp(raw);
        }

        if (raw.Equals("kb", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("kb ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("kb tool=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteKb(raw);
        }

        if (raw.Equals("shell", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("shell ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("shell command=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteShell(raw);
        }

        if (raw.Equals("debug", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("debug ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("debug op=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDebug(raw);
        }

        if (raw.Equals("git", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git tool=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("git op=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteGit(raw);
        }

        if (raw.Equals("ignite", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ignite ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ignite op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("autoi", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("autoi ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIgnite(raw);
        }

        if (raw.Equals("browser", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("browser ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("browser op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("internet_browser", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("internet_browser ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("web", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("web ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lynx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lynx ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteBrowser(raw);
        }

        if (raw.Equals("script", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("csx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("csx ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("script_report", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("script_report ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteScript(raw);
        }

        if (raw.Equals("ps1", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1 ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ise", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ise ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1 ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ps1_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ps1_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_put", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_put ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_check", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_check ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_last", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_last ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_ps1_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_ps1_help ", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePs1(raw);
        }

        if (raw.Equals("icm", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_icm", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_icm ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("command_module", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("command_module ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_aliases", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_aliases ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_resolve", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_resolve ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_invoke", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_invoke ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_exec", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_exec ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("icm_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("icm_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_icm_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_icm_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_icm_aliases", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_icm_aliases ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_icm_resolve", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_icm_resolve ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_icm_invoke", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_icm_invoke ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIcm(raw);
        }

        if (raw.Equals("files", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("file_manager", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("file_manager ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_ls", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_ls ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_cd", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_cd ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_up", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_up ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_stat", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_stat ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_tree", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_tree ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_text", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_text ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_read", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_read ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_search ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_roots", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_roots ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("files_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("files_clear ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_cd", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_cd ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_up", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_up ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_stat", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_stat ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_tree", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_tree ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_text", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_text ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_search ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_roots", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_roots ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_files_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_files_clear ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm_cd", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm_cd ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm_tree", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm_tree ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("fm_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fm_open ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteFiles(raw);
        }

        if (raw.Equals("onboard", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("explore_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("explore_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("explore", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("explore ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_onboard", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_onboard ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_scan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_scan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_refresh", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_refresh ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_rescan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_rescan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("onboard_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("onboard_clear ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_onboard_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_onboard_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_onboard_scan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_onboard_scan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_onboard_clear", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_onboard_clear ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteOnboard(raw);
        }

        if (raw.Equals("peel", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peel ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peel_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peel_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_peel", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_peel ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peel_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peel_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("peel_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("peel_apply ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_peel_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_peel_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_peel_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_peel_apply ", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePeel(raw);
        }

        if (raw.Equals("edit_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_plan_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_edit_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_edit_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_plan_draft", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan_draft ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_plan_validate", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan_validate ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_plan_apply ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_edit_plan_draft", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_edit_plan_draft ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_edit_plan_validate", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_edit_plan_validate ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_edit_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_edit_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_edit_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_edit_plan_apply ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteEditPlan(raw);
        }

        if (raw.Equals("analysis", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_analysis_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_analysis_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_analysis", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_analysis ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_clones", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_clones ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_correspondence", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_correspondence ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_corr", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_corr ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_semantic", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_semantic ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("analysis_semantic_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("analysis_semantic_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_analysis_clones", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_analysis_clones ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_analysis_correspondence", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_analysis_correspondence ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_analysis_semantic", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_analysis_semantic ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteAnalysis(raw);
        }

        if (raw.Equals("pressure", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pressure ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pressure op=", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePressure(raw);
        }

        if (raw.Equals("calendar", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("calendar ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("calendar op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clock", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clock ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clock op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("calendar_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("calendar_desk ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCalendar(raw);
        }

        if (raw.Equals("land", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("deep_link", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deep_link ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("deeplink", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deeplink ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("land_restore", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land_restore ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("land_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("land_goto", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land_goto ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("land_show", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land_show ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("land_go", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("land_go ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteLand(raw);
        }

        if (raw.Equals("pkg", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg op=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("packages", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("packages ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("package", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("package ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_find ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_remove", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_remove ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_update", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_update ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pkg_outdated", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pkg_outdated ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_find ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_remove", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_remove ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_update", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_update ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nuget_outdated", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nuget_outdated ", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePkg(raw);
        }

        if (raw.Equals("project", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("projects", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("projects ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_create", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_create ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_close", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_close ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_add_to_sln", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_add_to_sln ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("solution", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("solution ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_create", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_create ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_projects", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_projects ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sln_remove", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sln_remove ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteProject(raw);
        }

        if (raw.Equals("settings", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("options", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("options ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("prefs", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("prefs ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ide_settings", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ide_settings ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tools_options", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools_options ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("languages", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("languages ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("languages_page", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("languages_page ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_page", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_page ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_catalog", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_catalog ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_get", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_get ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_set", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_set ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_unset", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_unset ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("settings_which", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("settings_which ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("options_page", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("options_page ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lsp_probe", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lsp_probe ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lsp_status", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lsp_status ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lsp_install", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lsp_install ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lsp_ensure", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lsp_ensure ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("lsp_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("lsp_add ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteSettings(raw);
        }

        if (raw.Equals("restore", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("restore ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("restore_previous", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("restore_previous ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("desk_restore", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("desk_restore ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("restore_peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("restore_peek ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("open_recent", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("open_recent ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent_list ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteRestore(raw);
        }

        if (raw.Equals("intercom", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cide_intercom", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cide_intercom ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_send", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_send ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_ack", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_ack ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_history", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_history ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_presence", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_presence ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intercom_inbox", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intercom_inbox ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIntercom(raw);
        }

        if (raw.Equals("cide_presentation", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cide_presentation ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cide_presentation_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cide_presentation_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cide_presentation_set", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cide_presentation_set ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cide_presentation_get", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cide_presentation_get ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("presentation", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("presentation ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("presentation_set", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("presentation_set ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("presentation_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("presentation_scene ", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePresentation(raw);
        }

        if (raw.Equals("toolchain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_toolchain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_toolchain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_ensure", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_ensure ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_probe", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_probe ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_install", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_install ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_add", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_add ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("toolchain_which", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("toolchain_which ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteToolchain(raw);
        }

        if (raw.Equals("cockpit_host", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_host ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_cockpit_host", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_cockpit_host ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_start", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_start ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_stop", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_stop ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_host_start", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_host_start ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_host_stop", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_host_stop ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_host_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_host_scene ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCockpitHost(raw);
        }

        if (raw.Equals("qrh", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("eqrh", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eqrh ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_qrh", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_qrh ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_search ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_index", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_index ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_shelf", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_shelf ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("qrh_related", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("qrh_related ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("eqrh_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eqrh_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("eqrh_search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eqrh_search ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("eqrh_index", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eqrh_index ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_qrh_open", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_qrh_open ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_qrh_search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_qrh_search ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_qrh_index", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_qrh_index ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteQrh(raw);
        }

        if (raw.Equals("webcam", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_webcam", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_webcam ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_frame", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_frame ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_burst", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_burst ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_av", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_av ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_screen", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_screen ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_window_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_window_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_window", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_window ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_audio", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_audio ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_transcribe", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_transcribe ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_ocr", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_ocr ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("webcam_analyze", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("webcam_analyze ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_webcam_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_webcam_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_webcam_frame", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_webcam_frame ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_webcam_window_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_webcam_window_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_webcam_window", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_webcam_window ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteWebcam(raw);
        }

        if (raw.Equals("evidence", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_build", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_build ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_test", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_test ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_publish", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_publish ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_shell", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_shell ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_csx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_csx ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_generic", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_generic ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("evidence_auto", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("evidence_auto ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence_build", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence_build ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence_test", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence_test ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence_publish", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence_publish ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence_shell", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence_shell ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_evidence_csx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_evidence_csx ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteEvidence(raw);
        }

        if (raw.Equals("domain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_card ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_card ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDomain(raw);
        }

        if (raw.Equals("edit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("anchor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("anchor ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("anchor path=", StringComparison.OrdinalIgnoreCase))
        {
            return RouteEdit(raw);
        }

        if (raw.Equals("deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deploy ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("deploy mode=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hard_deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hard_deploy ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("soft_deploy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("soft_deploy ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDeploy(raw);
        }

        if (raw.Equals("undo", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("undo ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("undo path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("redo", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("redo ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("redo path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("edit_history", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edit_history ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteUndo(raw);
        }

        if (raw.Equals("copy", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("copy ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("copy path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cut", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cut ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cut path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("paste", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("paste ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("paste path=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clipboard", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clipboard ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clip", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("clip ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clipboard_clear", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("clip_clear", StringComparison.OrdinalIgnoreCase))
        {
            return RouteClip(raw);
        }

        if (raw.Equals("back", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("back ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("forward", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("forward ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nav ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav_status", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent_files", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("recent_files ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("recent", StringComparison.OrdinalIgnoreCase))
        {
            return RouteNav(raw);
        }

        if (raw.Equals("find_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_all ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("findall", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("findall ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_in", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find_buffer", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buf_find_all", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("buffer_find_all", StringComparison.OrdinalIgnoreCase))
        {
            return RouteFindBuf(raw);
        }

        if (raw.Equals("find", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("find query=", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("search", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("search ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("search query=", StringComparison.OrdinalIgnoreCase))
        {
            if (LooksLikeBufferFindScope(raw))
                return RouteFindBuf(raw);
            return RouteFind(raw);
        }

        if (LooksLikeGotoAll(raw))
            return RouteGotoAll(raw);

        if (raw.Equals("ide", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ide ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("goto", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("goto ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("usages", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("usages ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("diagnostics ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("definition", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("definition ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("complete", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("complete ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("completions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("completions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("signature", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("signature_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("document_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("document_symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("doc_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbol", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hover", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hover ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbol_at", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol_at ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rename", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rename ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("code_actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("code_actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quickfix", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quickfix ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("apply_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_action ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("apply_code_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_code_action ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("related", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("related ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("semantic_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("semantic_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nav_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("nav_context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("workspace_nav", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("workspace_nav ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("subgraph", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("subgraph ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("project_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("resolve_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("resolve_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("resolve_project_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("resolve_project_root ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("workspace_root", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("workspace_root ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteIde(raw);
        }

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
