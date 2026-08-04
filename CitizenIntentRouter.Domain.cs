#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent domain|domain_desk|cdp_domain — domain ownership without Cursor MCP (go=domain place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteDomain(string raw)
    {
        var work = NormalizeDomainCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        var keyedCardId = ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "card")
            ?? ExtractKeyedValue(work, "name");
        // Keyed card=/id=/name= without explicit op → card (not silent scene + lying ack).
        // Lived 2026-08-04: "domain card=citizen" was Op=scene Path=citizen.
        if (string.IsNullOrWhiteSpace(op) && keyedCardId is { Length: > 0 })
            op = "card";
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("domain ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("domain_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_domain ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeDomainOp(op);

        if (!IsDomainOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "domain_op_unknown");

        var path = keyedCardId
            ?? ExtractKeyedValue(work, "focus")
            ?? ExtractKeyedValue(work, "hint")
            ?? ExtractKeyedValue(work, "q")
            ?? PositionalDomainId(work, op);

        return new Route(
            Verb.Domain,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "domain");
    }

    static string? PositionalDomainId(string work, string op)
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

    static string NormalizeDomainCompound(string raw)
    {
        foreach (var (prefix, op) in DomainCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "domain " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "domain" + rest;
            return "domain " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] DomainCompounds =
    [
        ("domain_scene", "scene"),
        ("domain_pulse", "pulse"),
        ("domain_list", "list"),
        ("domain_card", "card"),
        ("domain_desk", "scene"),
        ("cdp_domain_scene", "scene"),
        ("cdp_domain_pulse", "pulse"),
        ("cdp_domain_list", "list"),
        ("cdp_domain_card", "card")
    ];

    static string NormalizeDomainOp(string op) =>
        op switch
        {
            "desk" or "status" or "a" or "map" => "scene",
            "ids" => "list",
            "get" or "one" or "show" => "card",
            _ => op
        };

    static bool IsDomainOp(string? op) =>
        op is "scene" or "pulse" or "list" or "card";
}
