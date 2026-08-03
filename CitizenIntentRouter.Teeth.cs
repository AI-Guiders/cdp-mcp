#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent teeth|cdp_teeth — IdeTeethChannel (go=teeth).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteTeeth(string raw)
    {
        var work = NormalizeTeethCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("teeth ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_teeth ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeTeethOp(op);

        if (!IsTeethOp(op))
            return new Route(Verb.Teeth, raw, Ok: false, Reason: "teeth_op_unknown");

        return new Route(
            Verb.Teeth,
            raw,
            Ok: true,
            Op: op,
            Go: "teeth");
    }

    static string NormalizeTeethCompound(string raw)
    {
        foreach (var (prefix, op) in TeethCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "teeth " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "teeth" + rest;
            return "teeth " + op + rest;
        }

        foreach (var alias in TeethAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "teeth";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "teeth " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("teeth", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("teeth ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] TeethCompounds =
    [
        ("teeth_scene", "scene"),
        ("teeth_tail", "tail"),
        ("teeth_explain", "explain"),
        ("cdp_teeth_scene", "scene"),
        ("cdp_teeth_tail", "tail"),
        ("cdp_teeth_explain", "explain")
    ];

    static readonly string[] TeethAliases =
    [
        "cdp_teeth"
    ];

    static string NormalizeTeethOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            "list" or "recent" => "tail",
            "why" => "explain",
            _ => op
        };

    static bool IsTeethOp(string? op) =>
        op is "scene" or "tail" or "explain";

    static bool IsTeethIntent(string raw)
    {
        if (raw.Equals("teeth", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("teeth ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in TeethAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in TeethCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
