#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent copy|cut|paste|clipboard — EditorComfort clip hand without Cursor MCP.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteClip(string raw)
    {
        var head = raw.Trim();
        string op;
        if (head.StartsWith("cut", StringComparison.OrdinalIgnoreCase))
            op = "cut";
        else if (head.StartsWith("paste", StringComparison.OrdinalIgnoreCase))
            op = "paste";
        else if (head.StartsWith("clipboard_clear", StringComparison.OrdinalIgnoreCase)
                 || head.StartsWith("clip_clear", StringComparison.OrdinalIgnoreCase))
            op = "clipboard_clear";
        else if (head.StartsWith("clipboard", StringComparison.OrdinalIgnoreCase)
                 || head.Equals("clip", StringComparison.OrdinalIgnoreCase)
                 || head.StartsWith("clip ", StringComparison.OrdinalIgnoreCase))
            op = "clipboard";
        else if (head.StartsWith("copy", StringComparison.OrdinalIgnoreCase))
            op = "copy";
        else
            op = ExtractKeyedValue(raw, "op") ?? "clipboard";

        op = op.Trim().ToLowerInvariant() switch
        {
            "cp" or "copy" => "copy",
            "cut" or "scissors" => "cut",
            "paste" or "p" => "paste",
            "clip" or "clipboard" or "clips" => "clipboard",
            "clipboard_clear" or "clip_clear" or "clear_clip" => "clipboard_clear",
            _ => op.Trim().ToLowerInvariant()
        };

        if (op is not "copy" and not "cut" and not "paste" and not "clipboard" and not "clipboard_clear")
            return new Route(Verb.Unknown, raw, Ok: false, Reason: "clip_op_unknown");

        // clear=true on clipboard → clipboard_clear
        if (op == "clipboard"
            && string.Equals(ExtractKeyedValue(raw, "clear"), "true", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "frame"))
            && string.IsNullOrWhiteSpace(ExtractKeyedValue(raw, "id")))
            op = "clipboard_clear";

        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var anchor = ExtractKeyedValue(raw, "anchor")
            ?? ExtractKeyedValue(raw, "at")
            ?? ExtractKeyedValue(raw, "from");
        var text = ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "body");
        var frame = ExtractKeyedValue(raw, "frame")
            ?? ExtractKeyedValue(raw, "id")
            ?? ExtractKeyedValue(raw, "clip");
        var place = ExtractKeyedValue(raw, "place") ?? ExtractKeyedValue(raw, "at_place");

        if ((op is "copy" or "cut")
            && string.IsNullOrWhiteSpace(path)
            && string.IsNullOrWhiteSpace(anchor)
            && string.IsNullOrWhiteSpace(text))
            return new Route(Verb.Clip, raw, Ok: false, Op: op, Reason: "clip_span_required", Go: "buffer");

        return new Route(
            Verb.Clip,
            raw,
            Ok: true,
            Op: op,
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? frame : anchor.Trim(),
            NewString: text,
            Scene: string.IsNullOrWhiteSpace(place) ? frame : place.Trim(),
            Go: "buffer");
    }
}
