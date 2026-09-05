#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeDeploy
{
    /// <summary>
    /// ADR-0211: a disposable deploy-worker clone records its origin install dir in
    /// deploy-worker-origin.txt — self-root resolves to the ORIGIN, not the clone,
    /// so deploy targets the real install instead of the worker's own folder.
    /// </summary>
    internal static string? ResolveSelfInstallRoot()
    {
        var baseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var workersRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "workers");
        var originFile = Path.Combine(baseDir, "deploy-worker-origin.txt");
        if (baseDir.StartsWith(workersRoot, StringComparison.OrdinalIgnoreCase)
            && File.Exists(originFile))
        {
            var origin = File.ReadAllText(originFile).Trim();
            if (!string.IsNullOrWhiteSpace(origin) && Directory.Exists(origin))
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(origin));
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return baseDir;
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
