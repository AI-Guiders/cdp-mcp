#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent onboard|onboard_desk|explore_desk|cdp_onboard — IdeOnboardChannel without Cursor MCP (go=onboard_desk place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteOnboard(string raw)
    {
        var work = NormalizeOnboardCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("onboard ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("onboard_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("explore_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("explore ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_onboard ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeOnboardOp(op);

        if (!IsOnboardOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "onboard_op_unknown");

        return new Route(
            Verb.Onboard,
            raw,
            Ok: true,
            Op: op,
            Go: "onboard_desk");
    }

    static string NormalizeOnboardCompound(string raw)
    {
        foreach (var (prefix, op) in OnboardCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "onboard " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "onboard" + rest;
            return "onboard " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] OnboardCompounds =
    [
        ("onboard_scene", "scene"),
        ("onboard_scan", "scan"),
        ("onboard_refresh", "scan"),
        ("onboard_rescan", "scan"),
        ("onboard_clear", "clear"),
        ("onboard_desk", "scene"),
        ("explore_desk", "scene"),
        ("cdp_onboard_scene", "scene"),
        ("cdp_onboard_scan", "scan"),
        ("cdp_onboard_clear", "clear")
    ];

    static string NormalizeOnboardOp(string op) =>
        op switch
        {
            "refresh" or "rescan" => "scan",
            "desk" or "status" or "show" or "pulse" or "map" => "scene",
            _ => op
        };

    static bool IsOnboardOp(string? op) =>
        op is "scene" or "scan" or "clear";
}
