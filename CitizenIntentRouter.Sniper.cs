#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent scope|peek|target|aim|scope_clear — EditSniper without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteSniper(string raw)
    {
        var head = raw.Trim();
        string? op;

        if (head.StartsWith("sniper", StringComparison.OrdinalIgnoreCase))
        {
            var rest = head.Length > 6 ? head[6..].TrimStart() : "";
            if (rest.Length == 0 || rest.StartsWith("status", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("show", StringComparison.OrdinalIgnoreCase))
                op = "status";
            else if (rest.StartsWith("clear", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase))
                op = "clear";
            else if (rest.StartsWith("scope", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("set", StringComparison.OrdinalIgnoreCase))
                op = "scope";
            else if (rest.StartsWith("peek", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("view", StringComparison.OrdinalIgnoreCase))
                op = "peek";
            else if (rest.StartsWith("target", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("outline", StringComparison.OrdinalIgnoreCase))
                op = "target";
            else if (rest.StartsWith("aim", StringComparison.OrdinalIgnoreCase))
                op = "aim";
            else if (ExtractKeyedValue(raw, "from") is { Length: > 0 }
                || ExtractKeyedValue(raw, "anchor") is { Length: > 0 })
                op = "scope";
            else
                op = ExtractKeyedValue(raw, "op") ?? "status";
        }
        else if (head.StartsWith("scope_clear", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("sniperclear", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("sniper_clear", StringComparison.OrdinalIgnoreCase))
            op = "clear";
        else if (head.StartsWith("scope", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            op = "scope";
        else if (head.Equals("peek", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("peek ", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("peek wire=", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("peek pad=", StringComparison.OrdinalIgnoreCase))
            op = "peek";
        else if (head.StartsWith("target", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("outline", StringComparison.OrdinalIgnoreCase))
            op = "target";
        else if (head.StartsWith("aim", StringComparison.OrdinalIgnoreCase))
            op = "aim";
        else
            op = ExtractKeyedValue(raw, "op") ?? "status";

        op = op.Trim().ToLowerInvariant() switch
        {
            "scope" or "set" or "lock" => "scope",
            "peek" or "view" => "peek",
            "target" or "outline" => "target",
            "aim" => "aim",
            "clear" or "scope_clear" or "sniperclear" or "sniper_clear" => "clear",
            "status" or "show" => "status",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "scope" and not "peek" and not "target" and not "aim" and not "clear" and not "status")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "sniper_op_unknown");

        var from = ExtractKeyedValue(raw, "from")
            ?? ExtractKeyedValue(raw, "anchor")
            ?? ExtractKeyedValue(raw, "wire")
            ?? ExtractKeyedValue(raw, "at");
        var till = ExtractKeyedValue(raw, "till")
            ?? ExtractKeyedValue(raw, "to");
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var pad = ExtractKeyedValue(raw, "pad");

        return new Route(
            Verb.Sniper,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: string.IsNullOrWhiteSpace(from) ? null : from.Trim(),
            NewString: string.IsNullOrWhiteSpace(till) ? null : till.Trim(),
            Detail: string.IsNullOrWhiteSpace(pad) ? null : pad.Trim(),
            Go: "scope");
    }
}
