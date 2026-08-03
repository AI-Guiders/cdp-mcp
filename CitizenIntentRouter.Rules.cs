#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent rules|standing|cdp_rules — IdeRulesChannel without Cursor MCP (go=rules place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteRules(string raw)
    {
        var work = NormalizeRulesCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("rules ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("rules_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("standing ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_rules ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeRulesOp(op);

        if (!IsRulesOp(op))
            return new Route(Verb.Rules, raw, Ok: false, Reason: "rules_op_unknown");

        var path = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "card")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "focus")
            ?? ExtractKeyedValue(work, "hint")
            ?? ExtractKeyedValue(work, "q")
            ?? PositionalRulesId(work, op);

        return new Route(
            Verb.Rules,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "rules");
    }

    static string? PositionalRulesId(string work, string op)
    {
        if (op is not ("card" or "pulse" or "scene"))
            return null;

        var sp = work.IndexOf(' ');
        if (sp < 0) return null;
        var rest = work[(sp + 1)..].Trim();
        if (rest.StartsWith(op + " ", StringComparison.OrdinalIgnoreCase))
            rest = rest[(op.Length + 1)..].Trim();
        else if (rest.Equals(op, StringComparison.OrdinalIgnoreCase))
            return null;

        var headSp = rest.IndexOf(' ');
        var head = headSp < 0 ? rest : rest[..headSp];
        if (head.Length == 0 || head.Contains('=', StringComparison.Ordinal))
            return null;
        return head.Trim().Trim('"');
    }

    static string NormalizeRulesCompound(string raw)
    {
        foreach (var (prefix, op) in RulesCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "rules " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "rules" + rest;
            return "rules " + op + rest;
        }

        foreach (var alias in RulesAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "rules";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "rules " + raw[alias.Length..].TrimStart();
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] RulesCompounds =
    [
        ("rules_scene", "scene"),
        ("rules_pulse", "pulse"),
        ("rules_list", "list"),
        ("rules_card", "card"),
        ("rules_desk", "scene"),
        ("cdp_rules_scene", "scene"),
        ("cdp_rules_pulse", "pulse"),
        ("cdp_rules_list", "list"),
        ("cdp_rules_card", "card")
    ];

    static readonly string[] RulesAliases =
    [
        "standing",
        "healthy_agent",
        "cdp_rules"
    ];

    static string NormalizeRulesOp(string op) =>
        op switch
        {
            "desk" or "status" or "a" or "map" => "scene",
            "ids" => "list",
            "get" or "one" or "show" => "card",
            _ => op
        };

    static bool IsRulesOp(string? op) =>
        op is "scene" or "pulse" or "list" or "card";
}
