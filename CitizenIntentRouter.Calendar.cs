#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent calendar|clock — host-local clock/month without Cursor MCP (go=calendar place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteCalendar(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (raw.StartsWith("calendar ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("clock ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("calendar_desk ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = raw.IndexOf(' ');
                var rest = sp < 0 ? "" : raw[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeCalendarOp(op);

        if (!IsCalendarOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "calendar_op_unknown");

        return new Route(
            Verb.Calendar,
            raw,
            Ok: true,
            Op: op,
            Go: "calendar");
    }

    static string NormalizeCalendarOp(string op) =>
        op switch
        {
            "status" or "desk" or "list" => "scene",
            "a" or "clock" or "local" => "pulse",
            "grid" => "month",
            _ => op
        };

    static bool IsCalendarOp(string? op) =>
        op is "scene" or "pulse" or "month";
}
