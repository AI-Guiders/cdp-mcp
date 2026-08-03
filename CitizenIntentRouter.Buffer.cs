#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent read|close|buffers|doc_diagnostics — DocumentEditPlane core without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteBuffer(string raw)
    {
        var head = raw.Trim();
        string? op;

        if (head.StartsWith("buffer ", StringComparison.OrdinalIgnoreCase)
            || head.Equals("buffer", StringComparison.OrdinalIgnoreCase))
        {
            var rest = head.Length > 6 ? head[6..].TrimStart() : "";
            if (rest.Length == 0)
                op = "scene";
            else if (rest.StartsWith("read", StringComparison.OrdinalIgnoreCase))
                op = "read";
            else if (rest.StartsWith("close", StringComparison.OrdinalIgnoreCase))
                op = "close";
            else if (rest.StartsWith("scene", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("buffers", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("list", StringComparison.OrdinalIgnoreCase))
                op = "scene";
            else if (rest.StartsWith("diagnostics", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("diags", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("diag", StringComparison.OrdinalIgnoreCase))
                op = "diagnostics";
            else
                op = ExtractKeyedValue(raw, "op") ?? "scene";
        }
        else if (head.StartsWith("doc_read", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_read", StringComparison.OrdinalIgnoreCase)
            || head.Equals("read", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("read ", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("read path=", StringComparison.OrdinalIgnoreCase))
            op = "read";
        else if (head.StartsWith("doc_close", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_close", StringComparison.OrdinalIgnoreCase)
            || head.Equals("close", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("close ", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("close path=", StringComparison.OrdinalIgnoreCase))
            op = "close";
        else if (head.Equals("buffers", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffers ", StringComparison.OrdinalIgnoreCase)
            || head.Equals("doc_scene", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("doc_scene", StringComparison.OrdinalIgnoreCase)
            || head.Equals("buffer_scene", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_scene", StringComparison.OrdinalIgnoreCase))
            op = "scene";
        else if (head.StartsWith("doc_diagnostics", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buffer_diagnostics", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buf_diagnostics", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("buf_diags", StringComparison.OrdinalIgnoreCase))
            op = "diagnostics";
        else
            op = ExtractKeyedValue(raw, "op") ?? "scene";

        op = op.Trim().ToLowerInvariant() switch
        {
            "read" or "doc_read" or "buffer_read" => "read",
            "close" or "doc_close" or "buffer_close" => "close",
            "scene" or "buffers" or "list" or "doc_scene" or "buffer_scene" => "scene",
            "diagnostics" or "diags" or "diag" or "doc_diagnostics" or "buffer_diagnostics"
                or "buf_diagnostics" or "buf_diags" => "diagnostics",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "read" and not "close" and not "scene" and not "diagnostics")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "buffer_op_unknown");

        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var docId = ExtractKeyedValue(raw, "doc_id") ?? ExtractKeyedValue(raw, "doc");
        var start = ExtractKeyedValue(raw, "start_line") ?? ExtractKeyedValue(raw, "from_line");
        var end = ExtractKeyedValue(raw, "end_line") ?? ExtractKeyedValue(raw, "to_line");

        return new Route(
            Verb.Buffer,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            OldString: string.IsNullOrWhiteSpace(docId) ? null : docId.Trim(),
            Detail: string.IsNullOrWhiteSpace(start) ? null : start.Trim(),
            NewString: string.IsNullOrWhiteSpace(end) ? null : end.Trim(),
            Go: "buffer");
    }
}
