#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent ide|goto|usages|diagnostics|complete|rename|actions — bare IDE without Cursor Roslyn MCP.</summary>
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

        if (op is "go_to_definition" or "find_usages" or "get_completions" or "get_signature_help"
            or "get_symbol_at_position" or "rename_symbol" or "code_actions" or "apply_code_action")
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

        if (op is "get_completions" or "get_signature_help" or "get_symbol_at_position"
            or "rename_symbol" or "code_actions" or "apply_code_action")
        {
            if (ExtractKeyedValue(raw, "column") is not { Length: > 0 }
                && ExtractKeyedValue(raw, "col") is not { Length: > 0 }
                && ExtractKeyedValue(raw, "c") is not { Length: > 0 })
            {
                return new Route(
                    Verb.Ide,
                    raw,
                    Ok: false,
                    Op: op,
                    Path: path,
                    Go: "editor_scene",
                    Reason: "ide_column_required");
            }
        }

        if (op is "rename_symbol"
            && ExtractKeyedValue(raw, "new_name") is not { Length: > 0 }
            && ExtractKeyedValue(raw, "name") is not { Length: > 0 }
            && ExtractKeyedValue(raw, "to") is not { Length: > 0 })
        {
            return new Route(
                Verb.Ide,
                raw,
                Ok: false,
                Op: op,
                Path: path,
                Go: "editor_scene",
                Reason: "ide_new_name_required");
        }

        if (op is "apply_code_action"
            && ExtractKeyedValue(raw, "action_index") is not { Length: > 0 }
            && ExtractKeyedValue(raw, "index") is not { Length: > 0 }
            && ExtractKeyedValue(raw, "i") is not { Length: > 0 })
        {
            return new Route(
                Verb.Ide,
                raw,
                Ok: false,
                Op: op,
                Path: path,
                Go: "editor_scene",
                Reason: "ide_action_index_required");
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

        if (raw.Equals("complete", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("complete ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("completions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("completions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("completion", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("completion ", StringComparison.OrdinalIgnoreCase))
            return "complete";

        if (raw.Equals("signature", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("signature_help", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("signature_help ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sighelp", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sighelp ", StringComparison.OrdinalIgnoreCase))
            return "signature";

        if (raw.Equals("symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("document_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("document_symbols ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("doc_symbols", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("doc_symbols ", StringComparison.OrdinalIgnoreCase))
            return "symbols";

        if (raw.Equals("symbol", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("hover", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("hover ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("symbol_at", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("symbol_at ", StringComparison.OrdinalIgnoreCase))
            return "symbol";

        if (raw.Equals("rename", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rename ", StringComparison.OrdinalIgnoreCase))
            return "rename";

        if (raw.Equals("actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("code_actions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("code_actions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quickfix", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quickfix ", StringComparison.OrdinalIgnoreCase))
            return "actions";

        if (raw.Equals("apply_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_action ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("apply_code_action", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("apply_code_action ", StringComparison.OrdinalIgnoreCase))
            return "apply_action";

        return null;
    }

    static string NormalizeIdeOp(string op) =>
        op switch
        {
            "goto" or "def" or "definition" or "god" or "go_to_definition" => "go_to_definition",
            "usages" or "refs" or "references" or "find_usages" or "usage" => "find_usages",
            "diagnostics" or "diags" or "diag" or "get_diagnostics" or "errors" => "get_diagnostics",
            "complete" or "completion" or "completions" or "get_completions" or "intellisense" => "get_completions",
            "signature" or "signature_help" or "sighelp" or "get_signature_help" or "sig" => "get_signature_help",
            "symbols" or "document_symbols" or "get_document_symbols" or "doc_symbols" => "get_document_symbols",
            "symbol" or "hover" or "symbol_at" or "get_symbol_at_position" => "get_symbol_at_position",
            "rename" or "rename_symbol" => "rename_symbol",
            "actions" or "code_actions" or "quickfix" or "get_code_actions" => "code_actions",
            "apply_action" or "apply_code_action" or "apply" => "apply_code_action",
            _ => op
        };

    static bool IsIdeOp(string? op) =>
        op is "go_to_definition" or "find_usages" or "get_diagnostics"
            or "get_completions" or "get_signature_help" or "get_document_symbols"
            or "get_symbol_at_position" or "rename_symbol" or "code_actions" or "apply_code_action";
}
