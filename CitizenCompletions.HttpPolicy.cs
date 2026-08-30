#nullable enable
using System.Net;
using Polly;
using Polly.Retry;

namespace CdpMcp;

/// <summary>
/// Per-turn HTTP budgets (not one blunt HttpClient.Timeout).
/// Headers = TTFT; Idle = stall between SSE lines; Overall = hard cap.
/// SoftFL reconnect: transient timeout/5xx/network → retry with backoff (Cursor-like).
/// </summary>
internal static partial class CitizenCompletions
{
    /// <summary>Tests: shrink HeadersTimeout (TTFT).</summary>
    internal static TimeSpan? TestHeadersTimeout;

    /// <summary>Tests: shrink IdleTimeout (SSE stall).</summary>
    internal static TimeSpan? TestIdleTimeout;

    /// <summary>Tests: shrink OverallTimeout.</summary>
    internal static TimeSpan? TestOverallTimeout;

    /// <summary>Tests: force attempt count (default 3).</summary>
    internal static int? TestMaxAttempts;

    /// <summary>Bridge/UI: attempt (1-based fail), max, error — paint reconnecting.</summary>
    internal static Action<int, int, string?>? TransientRetryHook;

    internal static TimeSpan IdleTimeout => TestIdleTimeout ?? TimeSpan.FromSeconds(30);
    internal static TimeSpan OverallTimeout => TestOverallTimeout ?? TimeSpan.FromSeconds(90);

    /// <summary>Tests: shrink AgentOverallTimeout (MEAI tool rounds).</summary>
    internal static TimeSpan? TestAgentOverallTimeout;

    /// <summary>Agent multi-round dig/tools — lived SoftFL wall after concurrent: stream Overall=90s too tight.</summary>
    internal static TimeSpan AgentOverallTimeout =>
        TestAgentOverallTimeout ?? TimeSpan.FromSeconds(180);


    /// <summary>Wire stays snappy; Face dialog fat history needs longer TTFT.</summary>
    internal static TimeSpan HeadersTimeoutFor(CitizenTurnMode mode) =>
        TestHeadersTimeout
        ?? (mode == CitizenTurnMode.Dialog
            ? TimeSpan.FromSeconds(45)
            : TimeSpan.FromSeconds(20));

    /// <summary>Legacy alias — wire budget (tests that omit mode).</summary>
    internal static TimeSpan HeadersTimeout => HeadersTimeoutFor(CitizenTurnMode.Wire);

    internal static int MaxAttempts =>
        TestMaxAttempts is int n && n > 0 ? Math.Clamp(n, 1, 5) : 3;

    /// <summary>Linked to caller cancel + overall hard cap.</summary>
    internal static CancellationTokenSource CreateTurnCts(CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(OverallTimeout);
        return cts;
    }

    /// <summary>Linked to caller cancel + agent multi-round hard cap.</summary>
    internal static CancellationTokenSource CreateAgentTurnCts(CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(AgentOverallTimeout);
        return cts;
    }


    internal static bool IsTransientError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;
        var e = error.Trim();
        if (e.Equals("timeout", StringComparison.OrdinalIgnoreCase))
            return true;
        if (e.Equals("http_network", StringComparison.OrdinalIgnoreCase))
            return true;
        if (e.Equals("http_429", StringComparison.OrdinalIgnoreCase)
            || e.Equals("http_502", StringComparison.OrdinalIgnoreCase)
            || e.Equals("http_503", StringComparison.OrdinalIgnoreCase)
            || e.Equals("http_504", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    internal static bool IsTransientHttp(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    internal static TimeSpan RetryBackoff(int failedAttempt) =>
        TimeSpan.FromMilliseconds(failedAttempt switch
        {
            1 => 400,
            2 => 1200,
            _ => 2400
        });

    /// <summary>Cursor-like reconnect: retry transient once/twice, then surface last fail.</summary>
    static TurnResult WithTransientRetry(Func<TurnResult> once)
    {
        var max = MaxAttempts;
        var attempts = 0;
        var pipeline = new ResiliencePipelineBuilder<TurnResult>()
            .AddRetry(new RetryStrategyOptions<TurnResult>
            {
                MaxRetryAttempts = max - 1,
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(RetryBackoff(args.AttemptNumber)),
                OnRetry = args =>
                {
                    if (args.Outcome.Result is TurnResult r)
                        TransientRetryHook?.Invoke(args.AttemptNumber, max, r.Error);
                    return ValueTask.CompletedTask;
                },
                ShouldHandle = new PredicateBuilder<TurnResult>()
                    .HandleResult(r => !r.Ok && IsTransientError(r.Error)),
            })
            .Build();

        var result = pipeline.Execute(() =>
        {
            attempts++;
            return once();
        });
        return AnnotateReconnect(result, attempts);
    }

    static TurnResult AnnotateReconnect(TurnResult r, int attempts)
    {
        if (attempts <= 1)
            return r;
        var note = r.Ok
            ? " · reconnect ok attempts=" + attempts
            : " · reconnect exhausted attempts=" + attempts;
        var hint = string.IsNullOrWhiteSpace(r.Hint) ? note.TrimStart(' ', '·') : r.Hint + note;
        return r with { Hint = hint };
    }
}
