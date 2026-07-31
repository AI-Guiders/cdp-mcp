#nullable enable

namespace CdpMcp;

/// <summary>
/// Always-on guest-host OOM tooth + wake:
/// (1) native Cursor dialog reason=oom → click New Window;
/// (2) CDT :9222 down→up edge after MinDown → schedule oom-wake arm.
/// Starts from Program (not EnsureStarted) — same as HILD.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    static readonly TimeSpan OomWatchInterval = TimeSpan.FromSeconds(2);
    static readonly TimeSpan OomDialogCooldown = TimeSpan.FromSeconds(8);

    static CancellationTokenSource? OomWatchCts;
    static int OomWatchPort = IdeIgniteChannel.DefaultPort;
    static int OomNewWindowClicks;
    static int OomWakeScheduled;
    static long OomDialogLastClickTicks;
    static long OomWakeLastScheduleTicks;
    static bool? CdtWasUp;
    static DateTimeOffset? CdtDownSinceUtc;

    /// <summary>Test hook — New Window clicks on OOM dialog.</summary>
    internal static int OomNewWindowClickCount => Volatile.Read(ref OomNewWindowClicks);

    /// <summary>Test hook — OOM wake arms scheduled.</summary>
    internal static int OomWakeScheduleCount => Volatile.Read(ref OomWakeScheduled);

    /// <summary>Test hook — whether OOM watch loop is armed.</summary>
    internal static bool IsOomWatchRunning =>
        Volatile.Read(ref OomWatchCts) is { IsCancellationRequested: false };

    /// <summary>Start (or restart) always-on OOM tooth/wake from Program.</summary>
    internal static void StartOomWatch(int? port = null)
    {
        StopOomWatch();
        OomWatchPort = port is > 0 ? port.Value : IdeIgniteChannel.DefaultPort;
        Interlocked.Exchange(ref OomNewWindowClicks, 0);
        Interlocked.Exchange(ref OomWakeScheduled, 0);
        Interlocked.Exchange(ref OomDialogLastClickTicks, 0);
        Interlocked.Exchange(ref OomWakeLastScheduleTicks, 0);
        CdtWasUp = null;
        CdtDownSinceUtc = null;
        var cts = new CancellationTokenSource();
        Volatile.Write(ref OomWatchCts, cts);
        _ = Task.Run(() => OomWatchLoopAsync(OomWatchPort, cts.Token));
    }

    internal static void StopOomWatch()
    {
        var cts = Interlocked.Exchange(ref OomWatchCts, null);
        if (cts is null)
            return;

        try { cts.Cancel(); }
        catch { /* ignore */ }

        try { cts.Dispose(); }
        catch { /* ignore */ }
    }

    static async Task OomWatchLoopAsync(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(OomWatchInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                ProbeOomDialogNewWindow();
                await ProbeCdtRecoveryAsync(port, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ide_ignite] oom-watch probe failed: {ex.Message}");
            }
        }
    }

    static void ProbeOomDialogNewWindow()
    {
        if (InCooldown(OomDialogLastClickTicks, OomDialogCooldown))
            return;

        if (!IdeIgniteNativeDialogs.TryClickOomNewWindow())
            return;

        Interlocked.Increment(ref OomNewWindowClicks);
        Interlocked.Exchange(ref OomDialogLastClickTicks, DateTime.UtcNow.Ticks);
        // Treat dialog as hard evidence of host death — force down edge so Up→wake fires after recover.
        CdtWasUp = false;
        CdtDownSinceUtc ??= DateTimeOffset.UtcNow;
        Console.Error.WriteLine(
            $"[ide_ignite] oom-dialog New Window #{OomNewWindowClickCount}");
    }

    static async Task ProbeCdtRecoveryAsync(int port, CancellationToken ct)
    {
        var up = await IdeIgniteChannel.TryPingCdtAsync(port, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        if (CdtWasUp is null)
        {
            CdtWasUp = up;
            if (!up)
                CdtDownSinceUtc = now;
            return;
        }

        if (!up)
        {
            if (CdtWasUp == true)
                CdtDownSinceUtc = now;
            CdtWasUp = false;
            return;
        }

        // up == true
        var wasDown = CdtWasUp == false;
        var downLongEnough = CdtDownSinceUtc is { } since
                             && (now - since) >= IdeOomWake.MinDownDuration;
        CdtWasUp = true;
        CdtDownSinceUtc = null;

        if (!wasDown || !downLongEnough)
            return;

        if (InCooldown(OomWakeLastScheduleTicks, IdeOomWake.WakeCooldown))
            return;

        if (TryScheduleOomWake() is null)
            return;

        Interlocked.Increment(ref OomWakeScheduled);
        Interlocked.Exchange(ref OomWakeLastScheduleTicks, DateTime.UtcNow.Ticks);
        Console.Error.WriteLine(
            $"[ide_ignite] oom-wake scheduled #{OomWakeScheduleCount} port={port}");
    }

    /// <summary>Arm one-shot timer charge_mode=oom (system wake — not superseded).</summary>
    internal static object? TryScheduleOomWake()
    {
        EnsureLoaded();
        var dueSec = Math.Clamp(IdeOomWake.DefaultDueSeconds, 1, 60);
        var now = DateTimeOffset.UtcNow;
        var id = IdeOomWake.ArmIdPrefix
                 + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                 + "-" + Guid.NewGuid().ToString("N")[..6];

        IgniteArm arm;
        lock (Gate)
        {
            Arms.RemoveAll(a =>
                a.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");

            arm = new IgniteArm
            {
                Id = id,
                Event = "timer",
                Message = IdeIgniteChannel.ComposeOomWakeCharge(),
                ChargeMode = IdeOomWake.ChargeMode,
                Task = IdeOomWake.ArmTask,
                Once = true,
                LastOnce = false,
                OkOnly = true,
                SettleSeconds = 2,
                WaitSeconds = 90,
                DueUtc = now + TimeSpan.FromSeconds(dueSec),
                InRaw = $"{dueSec}s",
                Status = "armed",
                CreatedUtc = now,
                LastError = "cdt_recovered_after_down"
            };
            Arms.Add(arm);
            PersistUnlocked();
        }

        return Slim(arm);
    }
}
