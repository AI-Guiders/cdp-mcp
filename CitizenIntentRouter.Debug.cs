#nullable enable

namespace CdpMcp;

internal static partial class CitizenIntentRouter
{
    static Route RouteDebug(string raw)
    {
        var op = ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(op) && raw.StartsWith("debug ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["debug ".Length..].Trim();
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (IsDebugOp(head))
                op = head;
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        if (op is "status")
            op = "scene";
        else if (op is "list")
            op = "bp_list";
        else if (op is "cont")
            op = "continue";

        if (!IsDebugOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "debug_op_unknown");

        var path = ExtractKeyedValue(raw, "path") ?? ExtractKeyedValue(raw, "file_path");
        if (op is "bp_add" && string.IsNullOrWhiteSpace(path))
            return new Route(Verb.Debug, raw, Ok: false, Op: op, Path: path, Go: "debug", Reason: "debug_path_required");
        if (op is "bp_add" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "line")))
            return new Route(Verb.Debug, raw, Ok: false, Op: op, Path: path, Go: "debug", Reason: "debug_line_required");

        return new Route(
            Verb.Debug,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "debug");
    }

    static bool IsDebugOp(string? op) =>
        op is "scene" or "status"
            or "bp_set" or "bp_add" or "bp_remove" or "bp_list" or "bp_clear" or "list"
            or "launch" or "attach"
            or "continue" or "cont" or "stop" or "stop_context"
            or "step_over" or "step_into" or "step_out"
            or "stack" or "variables";
}
