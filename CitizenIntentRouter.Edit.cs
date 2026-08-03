#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent edit|anchor — buffer edit_op=anchor (precise mutate without Cursor Write / set_text).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteEdit(string raw)
    {
        var editOp = ExtractKeyedValue(raw, "edit_op") ?? ExtractKeyedValue(raw, "op");
        if (!string.IsNullOrWhiteSpace(editOp))
        {
            var normalized = editOp.Trim().ToLowerInvariant();
            if (normalized is "set_text" or "set-text" or "settext")
            {
                return new Route(
                    Verb.Refuse,
                    raw,
                    Ok: true,
                    Reason: "edit_refuse_set_text — use edit_op=anchor (or replace/create/append)");
            }

            if (normalized is not "anchor")
                return new Route(Verb.Unknown, raw, Ok: false, Reason: "edit_op_unsupported_" + normalized);
        }

        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var anchor = ExtractKeyedValue(raw, "anchor")
            ?? ExtractKeyedValue(raw, "at")
            ?? ExtractKeyedValue(raw, "wire");
        var text = ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "new")
            ?? ExtractKeyedValue(raw, "new_string");
        var place = ExtractKeyedValue(raw, "place") ?? ExtractKeyedValue(raw, "at_place");

        if (string.IsNullOrWhiteSpace(path))
            return new Route(Verb.Edit, raw, Ok: false, Reason: "edit_path_required", Go: "buffer");
        if (string.IsNullOrWhiteSpace(anchor))
            return new Route(Verb.Edit, raw, Ok: false, Path: path, Reason: "edit_anchor_required", Go: "buffer");
        if (text is null)
            return new Route(Verb.Edit, raw, Ok: false, Path: path, Detail: anchor, Reason: "edit_text_required", Go: "buffer");

        place = string.IsNullOrWhiteSpace(place) ? "replace" : place.Trim().ToLowerInvariant();
        place = place switch
        {
            "pre" or "before" => "before",
            "post" or "after" => "after",
            "replace" or "overwrite" or "swap" => "replace",
            _ => place
        };
        if (place is not "before" and not "after" and not "replace")
            return new Route(Verb.Edit, raw, Ok: false, Path: path, Detail: anchor, Reason: "edit_place_invalid", Go: "buffer");

        return new Route(
            Verb.Edit,
            raw,
            Ok: true,
            Path: path.Trim(),
            Detail: anchor.Trim(),
            NewString: text,
            Op: place,
            Go: "buffer");
    }
}
