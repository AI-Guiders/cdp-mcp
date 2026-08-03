#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent back|forward|nav|recent_files — EditorComfort nav without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteNav(string raw)
    {
        var head = raw.Trim();
        string op;
        if (head.StartsWith("forward", StringComparison.OrdinalIgnoreCase))
            op = "forward";
        else if (head.StartsWith("back", StringComparison.OrdinalIgnoreCase))
            op = "back";
        else if (head.StartsWith("recent_files", StringComparison.OrdinalIgnoreCase)
                 || head.Equals("recent", StringComparison.OrdinalIgnoreCase))
            op = "recent_files";
        else if (head.StartsWith("nav_status", StringComparison.OrdinalIgnoreCase)
                 || head.Equals("nav", StringComparison.OrdinalIgnoreCase)
                 || head.StartsWith("nav ", StringComparison.OrdinalIgnoreCase))
            op = ExtractKeyedValue(raw, "op") is { Length: > 0 } keyed
                ? keyed
                : "nav";
        else
            op = ExtractKeyedValue(raw, "op") ?? "nav";

        op = op.Trim().ToLowerInvariant() switch
        {
            "b" or "back" or "prev" => "back",
            "f" or "fwd" or "forward" or "next" => "forward",
            "status" or "nav" or "nav_status" => "nav",
            "recent" or "recent_files" or "mru" => "recent_files",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "back" and not "forward" and not "nav" and not "recent_files")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "nav_op_unknown");

        return new Route(
            Verb.Nav,
            raw,
            Ok: true,
            Op: op,
            Go: "buffer");
    }
}
