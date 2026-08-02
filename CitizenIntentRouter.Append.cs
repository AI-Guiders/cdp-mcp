#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent append path=… body="…" parse peel.</summary>
internal static partial class CitizenIntentRouter
{
    /// <summary>
    /// <c>append path=… body="…"</c> / text= / content= — body required (suffix).
    /// </summary>
    static bool TryParseAppend(
        string raw,
        out string? path,
        out string? body,
        out string? reason)
    {
        path = null;
        body = null;
        reason = null;

        path = ExtractKeyedValue(raw, "path");
        body = ExtractKeyedValue(raw, "body")
            ?? ExtractKeyedValue(raw, "text")
            ?? ExtractKeyedValue(raw, "content");

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "append_path_empty";
            return false;
        }

        if (string.IsNullOrEmpty(body))
        {
            reason = "append_body_empty";
            return false;
        }

        return true;
    }
}
