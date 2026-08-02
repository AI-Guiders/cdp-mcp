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
        Build,
        Test,
        Mcp,
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

        if (raw.Equals("mcp", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("mcp ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteMcp(raw);
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

    static string? ExtractKeyedValue(string raw, string key)
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
