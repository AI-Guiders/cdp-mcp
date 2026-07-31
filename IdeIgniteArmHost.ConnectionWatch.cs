#nullable enable

namespace CdpMcp;

/// <summary>
/// After a successful fire, keep watching Cursor for "Connection Problems" / Retry
/// overlays until the next ignition. Idle alone is not enough — the flake can appear
/// mid-turn while the agent is "Planning next moves".
/// </summary>
internal static partial class IdeIgniteArmHost
{
    static readonly TimeSpan ConnectionWatchInterval = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ConnectionRetryCooldown = TimeSpan.FromSeconds(5);

    static CancellationTokenSource? ConnectionWatchCts;
    static int ConnectionWatchPort = IdeIgniteChannel.DefaultPort;
    static int ConnectionRetryClicks;
    static long ConnectionLastClickTicks;

    /// <summary>Test hook — clicks recorded by the watch loop.</summary>
    internal static int ConnectionRetryClickCount => Volatile.Read(ref ConnectionRetryClicks);

    /// <summary>Test hook — whether a post-fire watch loop is armed.</summary>
    internal static bool IsConnectionWatchRunning =>
        Volatile.Read(ref ConnectionWatchCts) is { IsCancellationRequested: false };

    /// <summary>Start (or restart) overlay watch after a successful CDT fire.</summary>
    internal static void StartConnectionWatch(int port)
    {
        StopConnectionWatch();
        ConnectionWatchPort = port > 0 ? port : IdeIgniteChannel.DefaultPort;
        Interlocked.Exchange(ref ConnectionRetryClicks, 0);
        Interlocked.Exchange(ref ConnectionLastClickTicks, 0);
        var cts = new CancellationTokenSource();
        Volatile.Write(ref ConnectionWatchCts, cts);
        _ = Task.Run(() => ConnectionWatchLoopAsync(ConnectionWatchPort, cts.Token));
    }

    /// <summary>Stop watch before the next fire (or on host teardown).</summary>
    internal static void StopConnectionWatch()
    {
        var cts = Interlocked.Exchange(ref ConnectionWatchCts, null);
        if (cts is null)
            return;

        try { cts.Cancel(); }
        catch { /* ignore */ }

        try { cts.Dispose(); }
        catch { /* ignore */ }
    }

    static async Task ConnectionWatchLoopAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ConnectionWatchInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (InCooldown())
                continue;

            try
            {
                if (!await IdeIgniteChannel.TryDismissConnectionProblemsOnPortAsync(port, ct)
                        .ConfigureAwait(false))
                    continue;

                Interlocked.Increment(ref ConnectionRetryClicks);
                Interlocked.Exchange(ref ConnectionLastClickTicks, DateTime.UtcNow.Ticks);
                Console.Error.WriteLine(
                    $"[ide_ignite] connection-watch Retry #{ConnectionRetryClickCount} port={port}");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[ide_ignite] connection-watch probe failed: {ex.Message}");
            }
        }
    }

    static bool InCooldown()
    {
        var last = Interlocked.Read(ref ConnectionLastClickTicks);
        if (last == 0)
            return false;
        var elapsed = DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc);
        return elapsed < ConnectionRetryCooldown;
    }
}
