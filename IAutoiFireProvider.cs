#nullable enable

namespace CdpMcp;

/// <summary>
/// AutoIgnition fire provider (strategy). One provider per target seat:
///   Cursor  — inject into Cursor Composer via CDT (:9222) [IdeIgniteChannel.FireAsync]
///   OpenCode — native `opencode run -s &lt;session&gt;` wake [IdeIgniteChannel.FireToOpencodeAsync]
/// Dispatch picks a provider; Cursor is the default. Providers stay isolated (no cross-tangling).
/// </summary>
internal interface IAutoiFireProvider
{
    string Channel { get; }

    /// <summary>True when this provider should handle the current fire (config-gated).</summary>
    bool IsActive();

    /// <summary>Deliver the wake charge; returns a result shaped for ApplyFireOutcome.</summary>
    Task<object> FireAsync(string message, int waitSeconds, CancellationToken ct);
}
