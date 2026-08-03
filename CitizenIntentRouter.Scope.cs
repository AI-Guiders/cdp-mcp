#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent project_switch|ps|cdp_scope — IdeScopeChannel (go=project_switch; not bare scope=sniper).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteScope(string raw)
    {
        var work = NormalizeScopeCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("project_switch ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("ps ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("primary_scope ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("scope_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_scope ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeScopeOp(op);

        if (!IsScopeOp(op))
            return new Route(Verb.Scope, raw, Ok: false, Reason: "scope_op_unknown");

        return new Route(
            Verb.Scope,
            raw,
            Ok: true,
            Op: op,
            Go: "project_switch");
    }

    static string NormalizeScopeCompound(string raw)
    {
        foreach (var (prefix, op) in ScopeCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "project_switch " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "project_switch" + rest;
            return "project_switch " + op + rest;
        }

        foreach (var alias in ScopeAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "project_switch";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "project_switch " + raw[alias.Length..].TrimStart();
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] ScopeCompounds =
    [
        ("project_switch_scene", "scene"),
        ("project_switch_set", "set"),
        ("project_switch_recall", "recall"),
        ("project_switch_clear", "clear"),
        ("ps_scene", "scene"),
        ("ps_set", "set"),
        ("ps_recall", "recall"),
        ("ps_clear", "clear"),
        ("scope_desk_scene", "scene"),
        ("scope_desk_set", "set"),
        ("scope_desk_recall", "recall"),
        ("scope_desk_clear", "clear"),
        ("cdp_scope_scene", "scene"),
        ("cdp_scope_set", "set"),
        ("cdp_scope_recall", "recall"),
        ("cdp_scope_clear", "clear")
    ];

    static readonly string[] ScopeAliases =
    [
        "ps",
        "primary_scope",
        "scope_desk",
        "cdp_scope"
    ];

    static string NormalizeScopeOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            "latch" or "switch" or "arm" => "set",
            "get" or "peek" or "load" => "recall",
            "reset" or "disarm" => "clear",
            _ => op
        };

    static bool IsScopeOp(string? op) =>
        op is "scene" or "set" or "recall" or "clear";

    static bool IsScopeIntent(string raw)
    {
        if (raw.Equals("project_switch", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("project_switch ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in ScopeAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in ScopeCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
