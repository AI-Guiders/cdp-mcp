#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent postmortem|cdp_postmortem — IdePostmortemChannel (go=postmortem).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePostmortem(string raw)
    {
        var work = NormalizePostmortemCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("postmortem ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_postmortem ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizePostmortemOp(op);

        if (!IsPostmortemOp(op))
            return new Route(Verb.Postmortem, raw, Ok: false, Reason: "postmortem_op_unknown");

        return new Route(
            Verb.Postmortem,
            raw,
            Ok: true,
            Op: op,
            Go: "postmortem");
    }

    static string NormalizePostmortemCompound(string raw)
    {
        foreach (var (prefix, op) in PostmortemCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "postmortem " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "postmortem" + rest;
            return "postmortem " + op + rest;
        }

        foreach (var alias in PostmortemAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "postmortem";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "postmortem " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("postmortem", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("postmortem ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] PostmortemCompounds =
    [
        ("postmortem_scene", "scene"),
        ("postmortem_template", "template"),
        ("postmortem_draft", "draft"),
        ("postmortem_record", "record"),
        ("postmortem_list", "list"),
        ("cdp_postmortem_scene", "scene"),
        ("cdp_postmortem_template", "template"),
        ("cdp_postmortem_draft", "draft"),
        ("cdp_postmortem_record", "record"),
        ("cdp_postmortem_list", "list")
    ];

    static readonly string[] PostmortemAliases =
    [
        "cdp_postmortem"
    ];

    static string NormalizePostmortemOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            "axes" => "template",
            "preview" => "draft",
            "commit" or "write" => "record",
            "recent" => "list",
            _ => op
        };

    static bool IsPostmortemOp(string? op) =>
        op is "scene" or "template" or "draft" or "record" or "list";

    static bool IsPostmortemIntent(string raw)
    {
        if (raw.Equals("postmortem", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("postmortem ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in PostmortemAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in PostmortemCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
