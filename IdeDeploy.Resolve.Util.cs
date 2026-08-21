#nullable enable

namespace CdpMcp;

internal static partial class IdeDeploy
{
    static string NormalizeMode(string mode)
    {
        var m = mode.Trim().ToLowerInvariant();
        return m switch
        {
            "soft" or "s" or "stage" => "soft",
            "hard" or "h" or "kill" => "hard",
            "rollout" or "r" or "dual" => "rollout",
            "apply" or "a" or "pending" or "apply_pending" => "apply",
            _ => "hard"
        };
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
