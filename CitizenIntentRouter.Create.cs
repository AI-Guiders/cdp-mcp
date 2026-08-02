#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent create|write path=… body="…" [overwrite=true] parse peel.</summary>
internal static partial class CitizenIntentRouter
{
    /// <summary>
    /// <c>create path=… body="…"</c> / <c>write path=… text="…"</c> — optional overwrite=true.
    /// Empty body allowed (new empty file).
    /// </summary>
    static bool TryParseCreate(
        string raw,
        out string? path,
        out string? body,
        out bool overwrite,
        out string? reason)
    {
        path = null;
        body = null;
        overwrite = false;
        reason = null;

        path = ExtractKeyedValue(raw, "path");
        body = ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "content")
            ?? "";

        var ow = ExtractKeyedValue(raw, "overwrite");
        if (!string.IsNullOrWhiteSpace(ow)
            && (ow.Equals("true", StringComparison.OrdinalIgnoreCase)
                || ow.Equals("1", StringComparison.OrdinalIgnoreCase)
                || ow.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            overwrite = true;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "create_path_empty";
            return false;
        }

        return true;
    }
}
