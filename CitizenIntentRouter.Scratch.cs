#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent scratch — untitled under .cdp/scratch without Cursor Write.</summary>
internal static partial class CitizenIntentRouter
{
    static Route RouteScratch(string raw)
    {
        var ext = ExtractKeyedValue(raw, "ext");
        var text = ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "content");

        return new Route(
            Verb.Scratch,
            raw,
            Ok: true,
            Op: "scratch",
            Detail: string.IsNullOrWhiteSpace(ext) ? null : ext.Trim(),
            NewString: text,
            Go: "buffer");
    }
}
