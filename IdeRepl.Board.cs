#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeRepl
{
    /// <summary>CCL verbs peeled from Apply (soft-warn). null = not handled.</summary>
    static (Dictionary<string, JsonElement> Args, object? Direct)? TryBoard(
        string head,
        IReadOnlyList<string> tokens,
        Dictionary<string, JsonElement> merged)
        => TryBoardSeed(head, tokens, merged)
            ?? TryBoardClock(head, tokens, merged)
            ?? TryBoardCriteria(head, tokens, merged);
}
