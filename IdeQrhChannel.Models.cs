#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeQrhChannel
{
    public sealed record Step(string Text, string? Go = null, string? Action = null);

    public sealed record Page(
        string Id,
        string Shelf, // systems | abnormal | emergency
        string Title,
        string Condition,
        IReadOnlyList<string> Signals,
        IReadOnlyList<string> MemoryItems,
        IReadOnlyList<Step> Steps,
        IReadOnlyList<string> Related,
        IReadOnlyList<string> PackAnchors,
        string? LlmCue = null,
        IReadOnlyList<SuggestRule>? Suggest = null,
        bool Builtin = true);

    /// <summary>When phases/ecl match probe — raise page in SA suggest (overlay-friendly).</summary>
    public sealed record SuggestRule(
        IReadOnlyList<string>? Phases,
        IReadOnlyList<string>? Ecl,
        int Score);

    public sealed record Suggest(
        string? HotId,
        IReadOnlyList<string> RelatedIds,
        string Pulse);

    public sealed record Snap(
        bool Ok,
        string Pulse,
        int PageCount,
        Suggest Suggest,
        IReadOnlyList<object> Index);
}

