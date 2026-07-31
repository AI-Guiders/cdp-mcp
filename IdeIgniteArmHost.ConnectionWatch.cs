#nullable enable

namespace CdpMcp;

/// <summary>
/// After a successful fire, watch Cursor environment flakes until the next ignition:
/// (1) Composer "Connection Problems" / Retry overlay via CDT;
/// (2) native Electron stall dialog "The window is not responding" → Keep Waiting (Win32);
/// (3) OOM terminated dialog → New Window (also covered by always-on OomWatch).
/// Idle alone is not enough — these can appear mid-turn.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    static readonly TimeSpan ConnectionWatchInterval = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ConnectionRetryCooldown = TimeSpan.FromSeconds(5);
    static readonly TimeSpan StallKeepWaitingCooldown = TimeSpan.FromSeconds(8);

    static CancellationTokenSource? ConnectionWatchCts;
    static int ConnectionWatchPort = IdeIgniteChannel.DefaultPort;
    static int ConnectionRetryClicks;
    static int StallKeepWaitingClicks;
    static long ConnectionLastClickTicks;
    static long StallLastClickTicks;

    /// <summary>Test hook — Connection Problems Retry clicks.</summary>
    internal static int ConnectionRetryClickCount => Volatile.Read(ref ConnectionRetryClicks);

    /// <summary>Test hook — Keep Waiting clicks on stall dialog.</summary>
    internal static int StallKeepWaitingClickCount => Volatile.Read(ref StallKeepWaitingClicks);

    /// <summary>Test hook — whether a post-fire watch loop is armed.</summary>
    internal static bool IsConnectionWatchRunning =>
        Volatile.Read(ref ConnectionWatchCts) is { IsCancellationRequested: false };

    /// <summary>Start (or restart) overlay/native-dialog watch after a successful CDT fire.</summary>
    internal static void StartConnectionWatch(int port)
    {
        StopConnectionWatch();
        ConnectionWatchPort = port > 0 ? port : IdeIgniteChannel.DefaultPort;
        Interlocked.Exchange(ref ConnectionRetryClicks, 0);
        Interlocked.Exchange(ref StallKeepWaitingClicks, 0);
        Interlocked.Exchange(ref ConnectionLastClickTicks, 0);
        Interlocked.Exchange(ref StallLastClickTicks, 0);
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

            try
            {
                await ProbeConnectionRetryAsync(port, ct).ConfigureAwait(false);
                ProbeStallKeepWaiting();
                if (IdeIgniteNativeDialogs.TryClickOomNewWindow())
                {
                    Console.Error.WriteLine("[ide_ignite] connection-watch oom New Window");
                }
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

    static async Task ProbeConnectionRetryAsync(int port, CancellationToken ct)
    {
        if (InCooldown(ConnectionLastClickTicks, ConnectionRetryCooldown))
            return;

        if (!await IdeIgniteChannel.TryDismissConnectionProblemsOnPortAsync(port, ct)
                .ConfigureAwait(false))
            return;

        Interlocked.Increment(ref ConnectionRetryClicks);
        Interlocked.Exchange(ref ConnectionLastClickTicks, DateTime.UtcNow.Ticks);
        Console.Error.WriteLine(
            $"[ide_ignite] connection-watch Retry #{ConnectionRetryClickCount} port={port}");
    }

    static void ProbeStallKeepWaiting()
    {
        if (InCooldown(StallLastClickTicks, StallKeepWaitingCooldown))
            return;

        if (!IdeIgniteNativeDialogs.TryClickKeepWaiting())
            return;

        Interlocked.Increment(ref StallKeepWaitingClicks);
        Interlocked.Exchange(ref StallLastClickTicks, DateTime.UtcNow.Ticks);
        Console.Error.WriteLine(
            $"[ide_ignite] stall-dialog Keep Waiting #{StallKeepWaitingClickCount}");
    }

    static bool InCooldown(long lastTicks, TimeSpan cooldown)
    {
        if (lastTicks == 0)
            return false;
        var elapsed = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
        return elapsed < cooldown;
    }
}
