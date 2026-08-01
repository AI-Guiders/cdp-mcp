#nullable enable

namespace CdpMcp;

/// <summary>Timer loop + arm status hygiene (≤ADX soft-warn peel).</summary>
internal static partial class IdeIgniteArmHost
{
    static async Task TimerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            List<IgniteArm> due;
            lock (Gate)
            {
                var now = DateTimeOffset.UtcNow;
                due = Arms.Where(a =>
                        a.Status == "armed"
                        && a.Event == "timer"
                        && a.DueUtc is { } d
                        && d <= now)
                    .Select(Clone)
                    .ToList();
            }

            foreach (var arm in due)
                QueueFire(arm, ok: true, pulse: "timer", detail: arm.InRaw);
        }
    }

    static void SetStatus(string id, string status, string? error, DateTimeOffset? fired = null)
    {
        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (a is null) return;
            a.Status = status;
            a.LastError = error;
            if (fired is { } f) a.FiredUtc = f;
            if (status == "firing") a.FiredUtc = DateTimeOffset.UtcNow;
            PersistUnlocked();
        }
    }

    internal static bool ShouldRequeueBusy(string eventName, string? error) =>
        string.Equals(error, "busy_timeout", StringComparison.Ordinal)
        && string.Equals(eventName, "timer", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldKeepVisibleErrorOnFireFail(bool once, bool lastOnce) =>
        lastOnce || !once;

    internal static TimeSpan BusyBackoff(int waitSeconds) =>
        TimeSpan.FromSeconds(Math.Clamp(waitSeconds / 3, 15, 60));

    static void RequeueAfterBusy(string id, string error, TimeSpan backoff)
    {
        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (a is null) return;
            a.Status = "armed";
            a.LastError = error;
            a.DueUtc = DateTimeOffset.UtcNow + backoff;
            PersistUnlocked();
            IdeTeethTape.Record(
                "wake_requeue",
                armId: a.Id,
                reason: a.Reason,
                detail: error);
        }
    }

    static void Remove(string id)
    {
        lock (Gate)
        {
            Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            PersistUnlocked();
        }

        CancelInFlightFire(id);
    }
}
