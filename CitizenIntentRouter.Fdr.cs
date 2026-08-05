#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent fdr|cdp_fdr — IdeFdrChannel (go=fdr).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteFdr(string raw)
    {
        var work = NormalizeFdrCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("fdr ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_fdr ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeFdrOp(op);

        if (!IsFdrOp(op))
            return new Route(Verb.Fdr, raw, Ok: false, Reason: "fdr_op_unknown");

        return new Route(
            Verb.Fdr,
            raw,
            Ok: true,
            Op: op,
            Go: "fdr");
    }

    static string NormalizeFdrCompound(string raw)
    {
        foreach (var (prefix, op) in FdrCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "fdr " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "fdr" + rest;
            return "fdr " + op + rest;
        }

        foreach (var alias in FdrAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "fdr";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "fdr " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("fdr", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fdr ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] FdrCompounds =
    [
        ("fdr_scene", "scene"),
        ("fdr_tail", "tail"),
        ("fdr_stats", "stats"),
        ("fdr_slow", "slow"),
        ("fdr_open", "open"),
        ("fdr_trace", "trace"),
        ("fdr_suggest", "suggest"),
        ("fdr_apply", "apply"),
        ("fdr_clear_overlay", "clear_overlay"),
        ("cdp_fdr_scene", "scene"),
        ("cdp_fdr_tail", "tail"),
        ("cdp_fdr_stats", "stats"),
        ("cdp_fdr_slow", "slow"),
        ("cdp_fdr_open", "open"),
        ("cdp_fdr_trace", "trace"),
        ("cdp_fdr_suggest", "suggest"),
        ("cdp_fdr_apply", "apply"),
        ("cdp_fdr_clear_overlay", "clear_overlay")
    ];

    static readonly string[] FdrAliases =
    [
        "cdp_fdr"
    ];

    static string NormalizeFdrOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            "list" or "recent" => "tail",
            "summary" => "stats",
            "incidents" => "slow",
            "inflight" or "ghost" => "open",
            "flight" or "call" => "trace",
            "thresholds" or "timeout_wake" => "suggest",
            "clear" => "clear_overlay",
            _ => op
        };

    static bool IsFdrOp(string? op) =>
        op is "scene" or "tail" or "stats" or "slow" or "open" or "trace" or "suggest" or "apply" or "clear_overlay";

    static bool IsFdrIntent(string raw)
    {
        if (raw.Equals("fdr", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("fdr ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in FdrAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in FdrCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
