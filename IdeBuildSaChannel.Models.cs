#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeBuildSaChannel
{
    static string NormDepth(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pulse" or "p" => "pulse",
        "full" or "raw" or "deep" => "full",
        _ => "slim"
    };

    static string NormScope(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "build" or "rebuild" => "build",
        "ship" or "scm" or "git" => "ship",
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
        string? Target,
        bool TargetOk,
        string? ScmRoot,
        string? Branch,
        bool ActiveDap,
        bool Stopped,
        IReadOnlyList<IdeReviewChannel.FileCard> Dirty,
        int SecretHits,
        int? Ahead,
        int? Behind);
}
