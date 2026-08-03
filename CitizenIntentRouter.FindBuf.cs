#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent find_all|buf_find — EditorComfort buffer Find without Cursor MCP (bare find stays IdeFindChannel).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteFindBuf(string raw)
    {
        var head = raw.Trim();
        string op;

        if (head.StartsWith("find_all", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("findall", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buf_find_all", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_find_all", StringComparison.OrdinalIgnoreCase))
            op = "find_all";
        else if (head.StartsWith("buf_find", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_find", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("find_in", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("find_buffer", StringComparison.OrdinalIgnoreCase))
            op = "find";
        else if (head.StartsWith("find ", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("search ", StringComparison.OrdinalIgnoreCase)
            || head.Equals("find", StringComparison.OrdinalIgnoreCase)
            || head.Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            // scope=buffer|file routes here; project/Ide stays RouteFind.
            op = "find";
        }
        else
            op = ExtractKeyedValue(raw, "op") ?? "find";

        op = op.Trim().ToLowerInvariant() switch
        {
            "find_all" or "findall" or "all" => "find_all",
            "find" or "buf_find" or "buffer_find" or "find_in" or "find_buffer" => "find",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "find" and not "find_all")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "findbuf_op_unknown");

        var query = ExtractKeyedValue(raw, "query")
            ?? ExtractKeyedValue(raw, "q")
            ?? ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "pattern");
        if (string.IsNullOrEmpty(query)
            && (head.StartsWith("find_all ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("buf_find ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("buffer_find ", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("find_in ", StringComparison.OrdinalIgnoreCase)))
        {
            query = ExtractPositionalFindBufQuery(head);
        }

        if (string.IsNullOrEmpty(query))
        {
            return new Route(
                Verb.FindBuf,
                raw,
                Ok: false,
                Op: op,
                Go: "buffer",
                Reason: "findbuf_query_required");
        }

        var path = ExtractKeyedValue(raw, "path") ?? ExtractKeyedValue(raw, "file");
        var scope = ExtractKeyedValue(raw, "scope") ?? ExtractKeyedValue(raw, "in") ?? "buffer";

        return new Route(
            Verb.FindBuf,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: query,
            Detail: string.IsNullOrWhiteSpace(scope) ? "buffer" : scope.Trim(),
            Go: "buffer");
    }

    /// <summary>find_all Needle path=a.cs → Needle (stop before first key=).</summary>
    static string? ExtractPositionalFindBufQuery(string head)
    {
        var sp = head.IndexOf(' ');
        if (sp < 0)
            return null;
        var rest = head[(sp + 1)..].Trim();
        if (rest.Length == 0)
            return null;
        if (rest.Contains('=', StringComparison.Ordinal))
        {
            // Prefer keyed; positional only when first token has no =
            var tokSp = rest.IndexOf(' ');
            var first = tokSp < 0 ? rest : rest[..tokSp];
            if (first.Contains('=', StringComparison.Ordinal))
                return null;
            return first.Trim().Trim('"');
        }

        return rest.Trim().Trim('"');
    }

    internal static bool LooksLikeBufferFindScope(string raw)
    {
        var scope = ExtractKeyedValue(raw, "scope") ?? ExtractKeyedValue(raw, "in");
        if (string.IsNullOrWhiteSpace(scope))
            return false;
        scope = scope.Trim();
        return scope.Equals("buffer", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("file", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("doc", StringComparison.OrdinalIgnoreCase);
    }
}
