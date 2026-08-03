#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent take — verify-then-ship span into peer (inverse of put).</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteTake(string raw)
    {
        var path = ExtractKeyedValue(raw, "path") ?? ExtractPath(raw);
        var anchor = ExtractKeyedValue(raw, "anchor") ?? ExtractKeyedValue(raw, "at") ?? ExtractKeyedValue(raw, "from");
        var sniper = ExtractKeyedValue(raw, "sniper");
        var useSniper = string.Equals(sniper, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sniper, "sniper", StringComparison.OrdinalIgnoreCase);

        return new Route(
            Verb.Take,
            raw,
            Ok: true,
            Op: "take",
            Path: string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Detail: string.IsNullOrWhiteSpace(anchor) ? null : anchor.Trim(),
            Scene: useSniper ? "sniper" : null,
            Go: "buffer");
    }
}
