#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent cdp_goto|goto_all — GoToAll (Ctrl+T/Q) without Cursor MCP. Bare goto path=+line= stays Ide go_to_definition.</summary>
internal static partial class CitizenIntentRouter
{
    static bool LooksLikeGotoAll(string raw)
    {
        foreach (var prefix in GotoAllPrefixes)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!raw.Equals("goto", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("goto ", StringComparison.OrdinalIgnoreCase))
            return false;

        // Definition locus — Ide Verb.
        if (ExtractKeyedValue(raw, "path") is { Length: > 0 }
            && ExtractKeyedValue(raw, "line") is { Length: > 0 })
            return false;

        if (ExtractKeyedValue(raw, "path") is { Length: > 0 })
            return false;

        return true;
    }

    static Route RouteGotoAll(string raw)
    {
        var work = NormalizeGotoAllCompound(raw);
        var query = ExtractKeyedValue(work, "query")
            ?? ExtractKeyedValue(work, "q")
            ?? ExtractKeyedValue(work, "text");

        if (string.IsNullOrWhiteSpace(query)
            && work.StartsWith("goto ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = work["goto ".Length..].Trim();
            if (rest.Length > 0 && !rest.Contains('=', StringComparison.Ordinal))
                query = rest;
            else if (rest.Length > 0)
            {
                var headSp = rest.IndexOf(' ');
                var head = headSp < 0 ? rest : rest[..headSp];
                if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                    query = head;
            }
        }

        if (string.IsNullOrWhiteSpace(query)
            && (work.StartsWith("cdp_goto ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("goto_all ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("go_to_all ", StringComparison.OrdinalIgnoreCase)
                || work.StartsWith("go_to ", StringComparison.OrdinalIgnoreCase)))
        {
            var sp = work.IndexOf(' ');
            var rest = sp < 0 ? "" : work[(sp + 1)..].Trim();
            var headSp = rest.IndexOf(' ');
            var head = headSp < 0 ? rest : rest[..headSp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                query = head;
        }

        if (string.IsNullOrWhiteSpace(query))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "goto_query_required", Go: "goto");

        var kind = ExtractKeyedValue(work, "kind")
            ?? ExtractKeyedValue(work, "filter");
        var peek = ExtractKeyedValue(work, "peek");
        var max = ExtractKeyedValue(work, "max");

        return new Route(
            Verb.GotoAll,
            raw,
            Ok: true,
            Op: string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToLowerInvariant(),
            Tool: query.Trim(),
            NewString: peek,
            Detail: max,
            Go: "goto");
    }

    static string NormalizeGotoAllCompound(string raw)
    {
        foreach (var (prefix, inject) in GotoAllCompounds)
        {
            if (raw.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                return inject.Length == 0 ? "goto" : "goto " + inject;
            if (!raw.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = raw[prefix.Length..].TrimStart();
            if (inject.Length == 0)
                return "goto " + rest;
            if (ExtractKeyedValue(raw, "kind") is { Length: > 0 }
                || ExtractKeyedValue(raw, "filter") is { Length: > 0 })
                return "goto " + rest;
            return "goto " + inject + " " + rest;
        }

        return raw;
    }

    static readonly string[] GotoAllPrefixes =
    [
        "cdp_goto",
        "goto_all",
        "go_to_all",
        "goto_feature",
        "goto_desk",
        "go_to"
    ];

    static readonly (string Prefix, string Inject)[] GotoAllCompounds =
    [
        ("goto_feature", "kind=feature"),
        ("goto_desk", ""),
        ("goto_all", ""),
        ("go_to_all", ""),
        ("cdp_goto", ""),
        ("go_to", "")
    ];
}
