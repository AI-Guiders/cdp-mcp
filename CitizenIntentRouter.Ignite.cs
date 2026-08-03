#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent ignite|autoi — continuity arm/list/resume without Cursor MCP (not CDT send/halt).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteIgnite(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "tool");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (raw.StartsWith("ignite ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("autoi ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = raw.IndexOf(' ');
                var rest = sp < 0 ? "" : raw[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "continuity" : op.Trim().ToLowerInvariant();
        op = NormalizeIgniteOp(op);

        if (IsIgniteRefuse(op))
        {
            return new Route(
                Verb.Refuse,
                raw,
                Ok: true,
                Op: op,
                Go: "ignite",
                Reason: "ignite_refuse_" + op);
        }

        if (!IsIgniteOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "ignite_op_unknown");

        if (op is "arm" && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "when") ?? ExtractKeyedValue(raw, "event")))
        {
            return new Route(
                Verb.Ignite,
                raw,
                Ok: false,
                Op: op,
                Go: "ignite",
                Reason: "ignite_when_required");
        }

        return new Route(
            Verb.Ignite,
            raw,
            Ok: true,
            Op: op,
            Go: "ignite");
    }

    static string NormalizeIgniteOp(string op) =>
        op switch
        {
            "scene" or "pulse" or "status" => "continuity",
            "schedule" or "wake" => "arm",
            "cancel" or "unarm" => "disarm",
            "arms" or "alarms" => "list",
            "clear_await" or "unawait" => "resume",
            _ => op
        };

    /// <summary>Observe + re-ARM insurance. CDT send/halt stay Cursor/human gates.</summary>
    static bool IsIgniteOp(string? op) =>
        op is "continuity" or "list" or "arm" or "disarm" or "resume";

    static bool IsIgniteRefuse(string? op) =>
        op is "send" or "fire" or "ignite" or "halt" or "stop" or "stop_world"
            or "chats" or "probe" or "caps" or "await_partner" or "await_operator" or "await"
            or "plateau" or "hygiene" or "autonomous_off" or "hild_off";
}
