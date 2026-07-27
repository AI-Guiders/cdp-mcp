#nullable enable
using System.Text.Json;
using DotnetDebug.Core;

namespace CdpMcp;

internal static partial class IdeDebugSaChannel
{
    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "bp" or "breakpoints" => "bp",
        "stop" or "stopped" => "stop",
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
        string? Workspace,
        string? Target,
        string? LaunchPath,
        string? Note,
        bool ActiveDap,
        bool Stopped,
        int LastStoppedThreadId,
        string? LastException,
        IReadOnlyList<BreakpointsStorage.BreakpointEntry> Breakpoints);
}
