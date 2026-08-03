#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent cockpit_host — Glass GUI start/stop without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteCockpitHost(string raw)
    {
        var work = NormalizeCockpitHostCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("cockpit_host ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_cockpit_host ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cockpit_start ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cockpit_stop ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? InferBareCockpitHostOp(work) : op.Trim().ToLowerInvariant();
        op = NormalizeCockpitHostOp(op);

        if (!IsCockpitHostOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "cockpit_host_op_unknown");

        var path = ExtractKeyedValue(work, "path") ?? ExtractKeyedValue(work, "exe");

        return new Route(
            Verb.CockpitHost,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "cockpit_host");
    }

    static string InferBareCockpitHostOp(string work)
    {
        if (work.Equals("cockpit_start", StringComparison.OrdinalIgnoreCase)
            || work.Equals("cdp_cockpit_start", StringComparison.OrdinalIgnoreCase))
            return "start";
        if (work.Equals("cockpit_stop", StringComparison.OrdinalIgnoreCase)
            || work.Equals("cdp_cockpit_stop", StringComparison.OrdinalIgnoreCase))
            return "stop";
        return "scene";
    }

    static string NormalizeCockpitHostCompound(string raw)
    {
        foreach (var (prefix, op) in CockpitHostCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "cockpit_host " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "cockpit_host" + rest;
            return "cockpit_host " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] CockpitHostCompounds =
    [
        ("cockpit_start", "start"),
        ("cockpit_stop", "stop"),
        ("cockpit_host_start", "start"),
        ("cockpit_host_stop", "stop"),
        ("cockpit_host_scene", "scene"),
        ("cdp_cockpit_host_start", "start"),
        ("cdp_cockpit_host_stop", "stop")
    ];

    static string NormalizeCockpitHostOp(string op) =>
        op switch
        {
            "up" or "open" or "launch" => "start",
            "down" or "close" or "kill" => "stop",
            "status" or "desk" or "show" or "pulse" => "scene",
            _ => op
        };

    static bool IsCockpitHostOp(string? op) =>
        op is "scene" or "start" or "stop";
}
