#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent share — IdeShare dual-axis (operator inbox / self shelf) without Cursor buffer dump.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteShare(string raw)
    {
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var with = ExtractKeyedValue(raw, "with") ?? ExtractKeyedValue(raw, "to");
        var from = ExtractKeyedValue(raw, "from");
        var body = ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "content")
            ?? ExtractKeyedValue(raw, "notes");
        var ask = ExtractKeyedValue(raw, "ask");
        var anchor = ExtractKeyedValue(raw, "anchor") ?? ExtractKeyedValue(raw, "at");

        // Pack: Detail=with|from|ask; NewString=body; Scene=ask when with present.
        var detail = !string.IsNullOrWhiteSpace(from)
            ? from.Trim()
            : !string.IsNullOrWhiteSpace(with)
                ? with.Trim()
                : ask;

        return new Route(
            Verb.Share,
            raw,
            Ok: true,
            Op: "share",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(detail) ? null : detail.Trim(),
            NewString: body,
            Scene: string.IsNullOrWhiteSpace(ask) ? null : ask.Trim(),
            Go: "buffer");
    }
}
