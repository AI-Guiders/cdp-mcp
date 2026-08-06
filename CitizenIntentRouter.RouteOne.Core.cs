#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Core — peel method_lines off RouteOne.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteCore(string raw)
    {
        if (raw.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase))
        {
            var cmd = raw.StartsWith("cmd=", StringComparison.OrdinalIgnoreCase)
                ? raw["cmd=".Length..].Trim()
                : raw["cmd ".Length..].Trim();
            cmd = cmd.Trim().Trim('"');
            if (cmd.Length == 0)
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "cmd_empty");
            if (IsWhoamiCmd(cmd))
            {
                // Who lives on intercom identity/scene — not TM board. Lived: Sierra cmd=whoami → refuse_non_plan_repl.
                return new Route(Verb.Intercom, raw, Ok: true, Cmd: cmd, Op: "scene", Go: "intercom");
            }

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

        // Bare SoftOrgan place — Sierra equal hands (lived: bare plan unrecognized; go=plan works).
        if (raw.Equals("plan", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("plan_desk", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_plan", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tm", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("task_manager", StringComparison.OrdinalIgnoreCase))
        {
            return new Route(Verb.Go, raw, Ok: true, Go: "plan");
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

        return null;
    }
}
