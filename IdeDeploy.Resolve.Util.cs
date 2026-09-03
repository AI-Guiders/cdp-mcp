#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    internal static string? ResolveSelfInstallRoot()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetDirectoryName(Path.GetFullPath(exe));
    }

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

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
        => args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    static bool IsTruthy(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return false;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) && b
                                   || string.Equals(el.GetString(), "1", StringComparison.Ordinal),
            _ => false
        };
    }
}
