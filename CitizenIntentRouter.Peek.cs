#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent file_peek|eyes|cdp_peek — read-only eyes (ADR-0201).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteFilePeek(string raw)
    {
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var anchor = ExtractKeyedValue(raw, "anchor") ?? ExtractKeyedValue(raw, "at");
        var offset = ExtractKeyedValue(raw, "offset") ?? ExtractKeyedValue(raw, "start_line");
        var limit = ExtractKeyedValue(raw, "limit") ?? ExtractKeyedValue(raw, "lines");
        var query = ExtractKeyedValue(raw, "query") ?? ExtractKeyedValue(raw, "pattern") ?? ExtractKeyedValue(raw, "q");

        return new Route(
            Verb.FilePeek,
            raw,
            Ok: true,
            Op: "cdp_peek",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? offset?.Trim() : anchor.Trim(),
            NewString: string.IsNullOrWhiteSpace(limit) ? null : limit.Trim(),
            Cmd: string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            Go: CdpPeekChannel.ToolName);
    }

    static bool IsFilePeekIntent(string raw)
    {
        if (raw.Equals("cdp_peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_peek ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("file_peek", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("file_peek ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("eyes", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eyes ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("eyes path=", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
