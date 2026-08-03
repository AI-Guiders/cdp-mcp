#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent intercom — cdp_intercom without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteIntercom(string raw)
    {
        var work = NormalizeIntercomCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("intercom ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cide_intercom ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "scene" : op.Trim().ToLowerInvariant();
        op = NormalizeIntercomOp(op);

        if (!IsIntercomOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "intercom_op_unknown");

        var body = ExtractKeyedValue(work, "body")
            ?? ExtractKeyedValue(work, "message")
            ?? ExtractKeyedValue(work, "text")
            ?? ExtractKeyedValue(work, "msg");
        if (op is "send" && string.IsNullOrWhiteSpace(body))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "intercom_body_required");

        var state = ExtractKeyedValue(work, "state")
            ?? ExtractKeyedValue(work, "status")
            ?? ExtractKeyedValue(work, "presence");
        if (op is "presence" && string.IsNullOrWhiteSpace(state))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "intercom_state_required");

        var limit = ExtractKeyedValue(work, "limit")
            ?? ExtractKeyedValue(work, "take")
            ?? ExtractKeyedValue(work, "n");

        return new Route(
            Verb.Intercom,
            raw,
            Ok: true,
            Op: op,
            Detail: limit,
            Go: "intercom");
    }

    static string NormalizeIntercomCompound(string raw)
    {
        foreach (var (prefix, op) in IntercomCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "intercom " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "intercom" + rest;
            return "intercom " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] IntercomCompounds =
    [
        ("intercom_send", "send"),
        ("intercom_scene", "scene"),
        ("intercom_ack", "ack"),
        ("intercom_history", "history"),
        ("intercom_presence", "presence"),
        ("intercom_inbox", "scene")
    ];

    static string NormalizeIntercomOp(string op) =>
        op switch
        {
            "get" or "inbox" or "status" or "desk" => "scene",
            "say" or "tx" => "send",
            "read" or "clear" => "ack",
            "line" or "journal" or "tail" => "history",
            "pulse_presence" => "presence",
            _ => op
        };

    static bool IsIntercomOp(string? op) =>
        op is "scene" or "send" or "ack" or "history" or "presence";
}
