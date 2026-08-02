#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent find|search — IdeFindChannel e2e dig without Cursor Grep.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteFind(string raw)
    {
        var head = raw.StartsWith("search", StringComparison.OrdinalIgnoreCase) ? "search" : "find";
        var op = ExtractKeyedValue(raw, "op");
        if (string.IsNullOrWhiteSpace(op) && raw.StartsWith(head + " ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw[(head.Length + 1)..].Trim();
            var sp = rest.IndexOf(' ');
            var token = sp < 0 ? rest : rest[..sp];
            if (token.Length > 0 && !token.Contains('=', StringComparison.Ordinal) && IsFindOpHead(token))
                op = token;
        }

        op = string.IsNullOrWhiteSpace(op) ? "run" : NormalizeFindOp(op.Trim().ToLowerInvariant());
        if (!IsFindOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "find_op_unknown");

        if (op is "last" or "clear")
            return new Route(Verb.Find, raw, Ok: true, Op: op, Go: "find_desk");

        var query = ExtractKeyedValue(raw, "query")
            ?? ExtractKeyedValue(raw, "q")
            ?? ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "pattern");

        if (string.IsNullOrWhiteSpace(query))
            query = ExtractPositionalFindQuery(raw, head);

        if (op is "run" && string.IsNullOrWhiteSpace(query))
        {
            return new Route(
                Verb.Find,
                raw,
                Ok: false,
                Op: op,
                Go: "find_desk",
                Reason: "find_query_required");
        }

        var path = ExtractKeyedValue(raw, "path")
            ?? ExtractKeyedValue(raw, "search_in")
            ?? ExtractKeyedValue(raw, "root");

        return new Route(
            Verb.Find,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "find_desk",
            NewString: query);
    }

    static string NormalizeFindOp(string op) =>
        op switch
        {
            "search" or "find" or "rg" or "grep" => "run",
            "history" => "last",
            "reset" => "clear",
            _ => op
        };

    static bool IsFindOpHead(string? head)
    {
        if (string.IsNullOrWhiteSpace(head))
            return false;
        return IsFindOp(NormalizeFindOp(head.Trim().ToLowerInvariant()));
    }

    static bool IsFindOp(string? op) =>
        op is "run" or "refine" or "last" or "clear";

    /// <summary>find Needle where=project → Needle (stop before first key=).</summary>
    static string? ExtractPositionalFindQuery(string raw, string head)
    {
        if (!raw.StartsWith(head + " ", StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = raw[(head.Length + 1)..].Trim();
        if (rest.Length == 0)
            return null;

        // Skip leading op token already consumed by RouteFind.
        var sp = rest.IndexOf(' ');
        var first = sp < 0 ? rest : rest[..sp];
        if (first.Length > 0 && !first.Contains('=', StringComparison.Ordinal) && IsFindOpHead(first))
            rest = sp < 0 ? "" : rest[(sp + 1)..].Trim();

        if (rest.Length == 0)
            return null;

        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            return end < 0 ? rest[1..] : rest[1..end];
        }

        var keyIdx = IndexOfKeyedArg(rest);
        var slice = keyIdx < 0 ? rest : rest[..keyIdx].Trim();
        return slice.Length == 0 ? null : slice;
    }

    static int IndexOfKeyedArg(string rest)
    {
        // First " key="-like token boundary (space + word + =).
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] != ' ')
                continue;
            var j = i + 1;
            while (j < rest.Length && rest[j] != ' ' && rest[j] != '=')
                j++;
            if (j < rest.Length && rest[j] == '=' && j > i + 1)
                return i;
        }

        return -1;
    }
}
