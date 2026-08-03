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

        if (raw.Equals("pressure", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pressure ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pressure op=", StringComparison.OrdinalIgnoreCase))
        {
            return RoutePressure(raw);
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
