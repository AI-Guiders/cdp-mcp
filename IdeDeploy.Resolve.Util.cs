#nullable enable

namespace CdpMcp;

internal static partial class IdeDeploy
{
    static string NormalizeMode(string mode)
    {
        var m = mode.Trim().ToLowerInvariant();
        return m is "soft" or "hard" or "rollout" ? m : "hard";
    }

    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
