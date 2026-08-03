#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent put — EditorComfort draft dump without Cursor Write.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RoutePut(string raw)
    {
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var text = ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "content");
        var frame = ExtractKeyedValue(raw, "frame")
            ?? ExtractKeyedValue(raw, "id")
            ?? ExtractKeyedValue(raw, "clip");
        var anchor = ExtractKeyedValue(raw, "anchor") ?? ExtractKeyedValue(raw, "at");
        var place = ExtractKeyedValue(raw, "place") ?? ExtractKeyedValue(raw, "at_place");
        var sniper = ExtractKeyedValue(raw, "sniper")
            ?? ExtractKeyedValue(raw, "dest");

        var useSniper = string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(place, "sniper", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(path)
            && string.IsNullOrWhiteSpace(anchor)
            && !useSniper)
            return new Route(Verb.Put, raw, Ok: false, Op: "put", Reason: "path_or_dest_required", Go: "buffer");

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(frame))
            return new Route(Verb.Put, raw, Ok: false, Op: "put", Path: path, Reason: "body_or_frame_required", Go: "buffer");

        // Pack: Scene=place|sniper flag; Detail=anchor|frame; Op flags overwrite/preserve via Scene bits avoided — reparse in host.
        var scene = useSniper ? "sniper" : place;
        return new Route(
            Verb.Put,
            raw,
            Ok: true,
            Op: "put",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? frame : anchor.Trim(),
            NewString: text,
            Scene: scene,
            Go: "buffer");
    }
}
