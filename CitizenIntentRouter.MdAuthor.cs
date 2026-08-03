#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent md_author|markdown_author|cdp_md_author — IdeMdAuthorChannel (go=md_author).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteMdAuthor(string raw)
    {
        var work = NormalizeMdAuthorCompound(raw);
        var op = ExtractKeyedValue(work, "op") ?? ExtractKeyedValue(work, "cmd");
        if (string.IsNullOrWhiteSpace(op))
        {
            if (work.StartsWith("md_author ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("md_author_desk ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("markdown_author ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("md_include ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("cdp_md_author ", StringComparison.OrdinalIgnoreCase))
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
        op = NormalizeMdAuthorOp(op);

        if (!IsMdAuthorOp(op))
            return new Route(Verb.MdAuthor, raw, Ok: false, Reason: "md_author_op_unknown");

        var path = ExtractKeyedValue(work, "path")
            ?? ExtractKeyedValue(work, "out")
            ?? ExtractKeyedValue(work, "scope")
            ?? PositionalMdAuthorPath(work, op);

        return new Route(
            Verb.MdAuthor,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "md_author");
    }

    static string? PositionalMdAuthorPath(string work, string op)
    {
        if (op is not ("check" or "expand" or "export"))
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

    static string NormalizeMdAuthorCompound(string raw)
    {
        foreach (var (prefix, op) in MdAuthorCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return "md_author " + op;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..];
            if (ExtractKeyedValue(raw, "op") is { Length: > 0 })
                return "md_author" + rest;
            return "md_author " + op + rest;
        }

        foreach (var alias in MdAuthorAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase))
                return "md_author";
            if (raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return "md_author " + raw[alias.Length..].TrimStart();
        }

        if (raw.Equals("md_author", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("md_author ", StringComparison.OrdinalIgnoreCase))
            return raw;

        return raw;
    }

    static readonly (string Prefix, string Op)[] MdAuthorCompounds =
    [
        ("md_author_scene", "scene"),
        ("md_author_check", "check"),
        ("md_author_expand", "expand"),
        ("md_author_export", "export"),
        ("md_author_desk", "scene"),
        ("cdp_md_author_scene", "scene"),
        ("cdp_md_author_check", "check"),
        ("cdp_md_author_expand", "expand"),
        ("cdp_md_author_export", "export")
    ];

    static readonly string[] MdAuthorAliases =
    [
        "md_author_desk",
        "markdown_author",
        "md_include",
        "cdp_md_author"
    ];

    static string NormalizeMdAuthorOp(string op) =>
        op switch
        {
            "desk" or "status" or "help" or "a" or "map" => "scene",
            "validate" => "check",
            _ => op
        };

    static bool IsMdAuthorOp(string? op) =>
        op is "scene" or "check" or "expand" or "export";

    static bool IsMdAuthorIntent(string raw)
    {
        if (raw.Equals("md_author", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("md_author ", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var alias in MdAuthorAliases)
        {
            if (raw.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var (prefix, _) in MdAuthorCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
