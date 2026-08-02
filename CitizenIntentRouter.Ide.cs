#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent ide|goto|usages|diagnostics — bare IDE nav without Cursor Roslyn MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteIde(string raw)
    {
        var op = ExtractKeyedValue(raw, "op") ?? ExtractKeyedValue(raw, "tool");
        if (string.IsNullOrWhiteSpace(op))
            op = InferIdeOpHead(raw);

        op = string.IsNullOrWhiteSpace(op) ? "" : NormalizeIdeOp(op.Trim().ToLowerInvariant());
        if (!IsIdeOp(op))
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "ide_op_unknown");

        var path = ExtractKeyedValue(raw, "path")
            ?? ExtractKeyedValue(raw, "file_path")
            ?? ExtractKeyedValue(raw, "file");
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Route(
                Verb.Ide,
                raw,
                Ok: false,
                Op: op,
                Go: "editor_scene",
                Reason: "ide_path_required");
        }

        if (op is "go_to_definition" or "find_usages")
        {
            if (ExtractKeyedValue(raw, "line") is not { Length: > 0 }
                && ExtractKeyedValue(raw, "l") is not { Length: > 0 })
            {
                return new Route(
                    Verb.Ide,
                    raw,
                    Ok: false,
                    Op: op,
                    Path: path,
                    Go: "editor_scene",
                    Reason: "ide_line_required");
            }
        }

        return new Route(
            Verb.Ide,
            raw,
            Ok: true,
            Op: op,
            Path: path,
            Go: "editor_scene");
    }

    static string? InferIdeOpHead(string raw)
    {
        if (raw.StartsWith("ide ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = raw["ide ".Length..].Trim();
            var sp = rest.IndexOf(' ');
            var head = sp < 0 ? rest : rest[..sp];
            if (head.Length > 0 && !head.Contains('=', StringComparison.Ordinal))
                return head;
            return null;
        }

        if (raw.Equals("goto", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("goto ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("definition", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("definition ", StringComparison.OrdinalIgnoreCase))
            return "goto";

        if (raw.Equals("usages", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("usages ", StringComparison.OrdinalIgnoreCase))
            return "usages";

        if (raw.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("diagnostics ", StringComparison.OrdinalIgnoreCase))
            return "diagnostics";

        return null;
    }

    static string NormalizeIdeOp(string op) =>
        op switch
        {
            "goto" or "def" or "definition" or "god" or "go_to_definition" => "go_to_definition",
            "usages" or "refs" or "references" or "find_usages" or "usage" => "find_usages",
            "diagnostics" or "diags" or "diag" or "get_diagnostics" or "errors" => "get_diagnostics",
            _ => op
        };

    static bool IsIdeOp(string? op) =>
        op is "go_to_definition" or "find_usages" or "get_diagnostics";
}
