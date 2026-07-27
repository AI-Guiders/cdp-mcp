#nullable enable
using System.Text.Json;
using DotNetBuildTest.Core;

namespace CdpMcp;

internal static partial class IdeTestSaChannel
{
    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "failed" or "fail" or "failures" => "failed",
        "last" or "last_run" => "last",
        _ => "session"
    };

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    sealed record Snap(
        bool Ok,
        string? Error,
        string? Target,
        TestRunCache.LastRun? Last);
}
