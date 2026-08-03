#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent icm|icm_desk|cdp_icm|command_module — IdeIcmChannel without Cursor MCP (go=icm_desk place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteIcm(string raw)
    {
        var work = NormalizeIcmCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("icm ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("icm_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_icm ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("command_module ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeIcmOp(op);

        if (!IsIcmOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "icm_op_unknown");

        var path = ExtractKeyedValue(work, "command_id")
            ?? ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "command");

        return new Route(
            Verb.Icm,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "icm_desk");
    }

    static string NormalizeIcmCompound(string raw)
    {
        foreach (var (prefix, op) in IcmCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "icm " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "icm" + rest;
            return "icm " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] IcmCompounds =
    [
        ("icm_scene", "scene"),
        ("icm_desk", "scene"),
        ("icm_aliases", "aliases"),
        ("icm_list", "aliases"),
        ("icm_map", "aliases"),
        ("icm_resolve", "resolve"),
        ("icm_invoke", "invoke"),
        ("icm_exec", "invoke"),
        ("icm_run", "invoke"),
        ("cdp_icm_scene", "scene"),
        ("cdp_icm_aliases", "aliases"),
        ("cdp_icm_list", "aliases"),
        ("cdp_icm_resolve", "resolve"),
        ("cdp_icm_invoke", "invoke"),
        ("command_module_scene", "scene"),
        ("command_module_aliases", "aliases"),
        ("command_module_resolve", "resolve"),
        ("command_module_invoke", "invoke")
    ];

    static string NormalizeIcmOp(string op) =>
        op switch
        {
            "list" or "map" => "aliases",
            "exec" or "run" => "invoke",
            "desk" or "status" or "show" or "pulse" => "scene",
            _ => op
        };

    static bool IsIcmOp(string? op) =>
        op is "scene" or "aliases" or "resolve" or "invoke";
}
