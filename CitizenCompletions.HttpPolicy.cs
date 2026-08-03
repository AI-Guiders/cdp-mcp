#nullable enable

namespace CdpMcp;

/// <summary>
/// Per-turn HTTP budgets (not one blunt HttpClient.Timeout).
/// Headers = TTFT; Idle = stall between SSE lines; Overall = hard cap.
/// </summary>
internal static partial class CitizenCompletions
{
    /// <summary>Tests: shrink HeadersTimeout (TTFT).</summary>
    internal static TimeSpan? TestHeadersTimeout;

    /// <summary>Tests: shrink IdleTimeout (SSE stall).</summary>
    internal static TimeSpan? TestIdleTimeout;

    /// <summary>Tests: shrink OverallTimeout.</summary>
    internal static TimeSpan? TestOverallTimeout;

    internal static TimeSpan HeadersTimeout => TestHeadersTimeout ?? TimeSpan.FromSeconds(20);
    internal static TimeSpan IdleTimeout => TestIdleTimeout ?? TimeSpan.FromSeconds(30);
    internal static TimeSpan OverallTimeout => TestOverallTimeout ?? TimeSpan.FromSeconds(90);

    /// <summary>Linked to caller cancel + overall hard cap.</summary>
    internal static CancellationTokenSource CreateTurnCts(CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(OverallTimeout);
        return cts;
    }
}
