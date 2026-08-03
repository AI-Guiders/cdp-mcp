#nullable enable

namespace CdpMcp;

/// <summary>Citizen @intent delete|rm|remove path=… [force=true] parse peel.</summary>
internal static partial class CitizenIntentRouter
{
    /// <summary>
    /// <c>delete path=…</c> / <c>rm path=…</c> / <c>remove path=…</c> — optional force=true for dirty buffer.
    /// </summary>
    static bool TryParseDelete(
        string raw,
        out string? path,
        out bool force,
        out string? reason)
    {
        path = null;
        force = false;
        reason = null;

        path = ExtractKeyedValue(raw, "path");
        var f = ExtractKeyedValue(raw, "force");
        if (!string.IsNullOrWhiteSpace(f)
            && (f.Equals("true", StringComparison.OrdinalIgnoreCase)
                || f.Equals("1", StringComparison.OrdinalIgnoreCase)
                || f.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            force = true;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "delete_path_empty";
            return false;
        }

        return true;
    }
}
