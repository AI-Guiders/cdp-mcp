#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent crm|callout|cdp_crm — IdeCrmChannel without Cursor MCP (go=crm place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteCrm(string raw)
    {
        var work = NormalizeCrmCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("crm ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("callout ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("crm_panel ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_crm ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeCrmOp(op);

        if (!IsCrmOp(op))
            return new Route(Verb.Crm, raw, Ok: false, Reason: "crm_op_unknown");

        var path = ExtractKeyedValue(work, "code")
            ?? ExtractKeyedValue(work, "callout")
            ?? ExtractKeyedValue(work, "response")
            ?? ExtractKeyedValue(work, "say")
            ?? ExtractKeyedValue(work, "ask")
            ?? ExtractKeyedValue(work, "what")
            ?? ExtractKeyedValue(work, "text")
            ?? ExtractKeyedValue(work, "ref")
            ?? ExtractKeyedValue(work, "ref_id")
            ?? ExtractKeyedValue(work, "kind")
            ?? PositionalCrmToken(work, op);

        return new Route(
            Verb.Crm,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "crm");
    }

    static string? PositionalCrmToken(string work, string op)
    {
        if (op is not ("call" or "respond" or "scene"))
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

    static string NormalizeCrmCompound(string raw)
    {
        foreach (var (prefix, op) in CrmCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "crm " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "crm" + rest;
            return "crm " + op + rest;
        }

        foreach (var alias in CrmAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "crm";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "crm " + raw[alias.Length..].TrimStart();
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] CrmCompounds =
    [
        ("crm_scene", "scene"),
        ("crm_call", "call"),
        ("crm_respond", "respond"),
        ("crm_last", "last"),
        ("crm_clear", "clear"),
        ("crm_lexicon", "lexicon"),
        ("cdp_crm_scene", "scene"),
        ("cdp_crm_call", "call"),
        ("cdp_crm_respond", "respond"),
        ("cdp_crm_last", "last"),
        ("cdp_crm_clear", "clear"),
        ("cdp_crm_lexicon", "lexicon")
    ];

    static readonly string[] CrmAliases =
    [
        "callout",
        "crm_panel",
        "cdp_crm"
    ];

    static string NormalizeCrmOp(string op) =>
        op switch
        {
            "desk" or "status" or "a" or "map" or "pulse" => "scene",
            "ask" or "open" => "call",
            "reply" or "say" => "respond",
            "codes" or "lex" => "lexicon",
            _ => op
        };

    static bool IsCrmOp(string? op) =>
        op is "scene" or "call" or "respond" or "last" or "clear" or "lexicon";

    static bool IsCrmIntent(string raw)
    {
        if (raw.Equals("crm", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("crm ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in CrmAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in CrmCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
