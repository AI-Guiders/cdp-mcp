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

}
