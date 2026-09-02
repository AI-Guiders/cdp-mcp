#nullable enable

using CdpMcp.Habitat;

namespace CdpMcp;

/// <summary>Citizen @intent file_peek|eyes|cdp_peek — read-only eyes (ADR-0201).</summary>
internal static partial class CitizenIntentRouter
{
    static readonly PrefixOpRule[] FilePeekIntentRules =
    [
        new("peek", "cdp_peek", "cdp_peek ", "file_peek", "file_peek ", "eyes", "eyes ", "eyes path="),
    ];
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

    static bool IsFilePeekIntent(string raw) =>
        PrefixOpTable.Match(raw.Trim(), FilePeekIntentRules) is not null;
}
