#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent undo|redo|edit_history — buffer EditorComfort without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteUndo(string raw)
    {
        var head = raw.Trim();
        string? op;
        if (head.StartsWith("redo", StringComparison.OrdinalIgnoreCase))
            op = "redo";
        else if (head.StartsWith("edit_history", StringComparison.OrdinalIgnoreCase))
            op = "history";
        else
            op = ExtractKeyedValue(raw, "op") ?? "undo";

        op = op.Trim().ToLowerInvariant() switch
        {
            "u" or "undo" or "revert" => "undo",
            "r" or "redo" or "unundo" => "redo",
            "h" or "history" or "stack" or "edit_history" => "history",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "undo" and not "redo" and not "history")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "undo_op_unknown");

        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        return new Route(
            Verb.Undo,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Go: "buffer");
    }
}
