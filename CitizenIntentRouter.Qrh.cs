#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent qrh|eqrh — eQRH handbook without Cursor MCP (go=qrh place-only).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteQrh(string raw)
    {
        var work = NormalizeQrhCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("qrh ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("eqrh ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_qrh ", StringComparison.OrdinalIgnoreCase))
            {
                var sp = work.IndexOf(' ');
                var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    op = head;
            }
        }

        op = string.IsNullOrWhiteSpace(op) ? "index" : op.Trim().ToLowerInvariant();
        op = NormalizeQrhOp(op);

        if (!IsQrhOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "qrh_op_unknown");

        var path = ExtractQrhPositional(work, op);

        return new Route(
            Verb.Qrh,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "qrh");
    }

    static string? ExtractQrhPositional(string work, string op)
    {
        // Prefer keyed values; positional only for open/search/shelf/related/remove.
        if (op is "open" or "related" or "remove")
        {
            return ExtractKeyedValue(work, "id")
                ?? ExtractKeyedValue(work, "page")
                ?? ExtractKeyedValue(work, "name")
                ?? ExtractKeyedValue(work, "from")
                ?? PositionalAfterOp(work, op);
        }

        if (op is "search")
        {
            return ExtractKeyedValue(work, "q")
                ?? ExtractKeyedValue(work, "query")
                ?? ExtractKeyedValue(work, "id")
                ?? PositionalAfterOp(work, op);
        }

        if (op is "shelf")
        {
            return ExtractKeyedValue(work, "shelf")
                ?? ExtractKeyedValue(work, "section")
                ?? ExtractKeyedValue(work, "id")
                ?? PositionalAfterOp(work, op);
        }

        return ExtractKeyedValue(work, "id")
            ?? ExtractKeyedValue(work, "q")
            ?? ExtractKeyedValue(work, "shelf");
    }

    static string? PositionalAfterOp(string work, string op)
    {
        // "qrh open intake-brief" / "eqrh search path"
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

    static string NormalizeQrhCompound(string raw)
    {
        foreach (var (prefix, op) in QrhCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "qrh " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "qrh" + rest;
            return "qrh " + op + rest;
        }

        return raw;
    }

    static readonly (string Prefix, string Op)[] QrhCompounds =
    [
        ("qrh_open", "open"),
        ("qrh_search", "search"),
        ("qrh_index", "index"),
        ("qrh_scene", "index"),
        ("qrh_shelf", "shelf"),
        ("qrh_related", "related"),
        ("eqrh_open", "open"),
        ("eqrh_search", "search"),
        ("eqrh_index", "index"),
        ("cdp_qrh_open", "open"),
        ("cdp_qrh_search", "search"),
        ("cdp_qrh_index", "index")
    ];

    static string NormalizeQrhOp(string op) =>
        op switch
        {
            "scene" or "list" or "catalog" or "desk" or "status" or "pulse" => "index",
            "page" or "show" or "get" => "open",
            "find" or "q" => "search",
            "section" => "shelf",
            "suggest" => "related",
            "rm" or "delete" => "remove",
            "upsert" => "add",
            "on" => "enable",
            "off" => "disable",
            _ => op
        };

    static bool IsQrhOp(string? op) =>
        op is "index" or "open" or "search" or "shelf" or "related"
            or "add" or "remove" or "overlay" or "enable" or "disable";
}
