#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent ps1|ise|ps1_scene — Ps1Scene habitat without Cursor MCP (go=ps1_scene place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePs1(string raw)
    {
        var work = NormalizePs1Compound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("ps1 ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("ise ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("ps1_scene ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("ps1_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_ps1_scene ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_ps1 ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizePs1Op(op);

        if (!IsPs1Op(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "ps1_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "name")
            ?? ExtractKeyedValue(work, "file");

        return new Route(
            Verb.Ps1,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "ps1_scene");
    }

    static string NormalizePs1Compound(string raw)
    {
        foreach (var (prefix, op) in Ps1Compounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "ps1 " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "ps1" + rest;
            return "ps1 " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] Ps1Compounds =
    [
        ("ps1_scene", "scene"),
        ("ps1_desk", "scene"),
        ("ps1_put", "put"),
        ("ps1_open", "open"),
        ("ps1_check", "check"),
        ("ps1_run", "run"),
        ("ps1_last", "last"),
        ("ps1_help", "help"),
        ("cdp_ps1_scene", "scene"),
        ("cdp_ps1_put", "put"),
        ("cdp_ps1_open", "open"),
        ("cdp_ps1_check", "check"),
        ("cdp_ps1_run", "run"),
        ("cdp_ps1_last", "last"),
        ("cdp_ps1_help", "help")
    ];

    static string NormalizePs1Op(string op) =>
        op switch
        {
            "map" or "status" or "list" or "desk" => "scene",
            "new" or "create" => "put",
            "parse" or "compile" => "check",
            "dryrun" or "dry_run" => "run",
            "report" => "last",
            _ => op
        };

    static bool IsPs1Op(string? op) =>
        op is "scene" or "put" or "open" or "check" or "run" or "last" or "help";
}
