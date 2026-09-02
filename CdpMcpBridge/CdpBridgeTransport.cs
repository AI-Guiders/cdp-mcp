namespace CdpMcpBridge;

internal sealed class CdpBridgeInvokeContext
{
    internal bool SuppressAutoStart { get; init; }
    internal int MaxAttempts { get; init; } = 2;
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    internal TimeSpan? TotalTimeout { get; init; }

    internal static CdpBridgeInvokeContext Default => new();

    internal static CdpBridgeInvokeContext DeployGap(CdpBridgeTiming timing) => new()
    {
        SuppressAutoStart = true,
        MaxAttempts = int.MaxValue,
        RetryDelay = timing.DeployGapRetryInterval,
        TotalTimeout = timing.DeployWaitTimeout
    };

    internal static CdpBridgeInvokeContext ServiceReady(CdpBridgeTiming timing) => new()
    {
        SuppressAutoStart = false,
        MaxAttempts = int.MaxValue,
        RetryDelay = timing.DeployPollInterval,
        TotalTimeout = timing.DeployWaitTimeout
    };
}

internal static class CdpBridgeTransport
{
    internal static async Task<T> WithRetryAsync<T>(
        CdpBridgeServiceEnsurer ensurer,
        CdpBridgeInvokeContext ctx,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        Exception? last = null;
        for (var attempt = 0; attempt < ctx.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ctx.TotalTimeout is { } total && DateTime.UtcNow - started > total)
                break;

            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (CdpBridgeServiceEnsurer.IsConnectionFailure(ex))
            {
                last = ex;
                var canEnsure = !ctx.SuppressAutoStart && !ensurer.ShouldSuppressAutoStart();
                if (canEnsure && attempt == 0)
                {
                    if (!await ensurer.TryEnsureRunningAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (ctx.MaxAttempts <= 2)
                            throw;
                    }
                }
                else if (ctx.MaxAttempts <= 2 && attempt >= 1)
                {
                    throw;
                }

                if (attempt + 1 >= ctx.MaxAttempts)
                    break;

                await Task.Delay(ctx.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("CdpBridgeTransport retry exhausted.");
    }
}
