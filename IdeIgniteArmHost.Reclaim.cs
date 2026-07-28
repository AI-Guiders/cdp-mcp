#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>
    /// Survivor after hard-deploy KillRunning: ensure TimerLoop + reclaim overdue/stuck firing
    /// (JSON survives remount; host does not).
    /// </summary>
    public static object WakeAfterHardDeploy()
    {
        EnsureStarted();
        // Host may already be running on survivor — still reclaim.
        var ids = ReclaimOverdue(TimeSpan.FromSeconds(3));
        return new
        {
            ok = true,
            op = "wake_after_hard_deploy",
            reclaimed = ids.Count,
            ids,
            arms = SceneSlice(),
            hint = ids.Count > 0
                ? "Reclaimed overdue/stuck arms — TimerLoop will fire when due."
                : "Ignite host awake; no overdue arms to reclaim."
        };
    }

    /// <summary>
    /// Consume remount-wake pending for this seat and arm a one-shot timer with charge_mode=remount.
    /// Called on process boot (EnsureStarted). Returns null when no pending.
    /// </summary>
    internal static object? TryScheduleRemountInitializedWake(string? seatOverride = null)
    {
        var seat = IdeRemountWake.NormalizeSeat(seatOverride ?? Seat);
        if (!IdeRemountWake.TryConsumePending(seat, out var pending))
            return null;

        EnsureLoaded();
        var dueSec = Math.Clamp(IdeRemountWake.DefaultDueSeconds, 1, 60);
        var now = DateTimeOffset.UtcNow;
        var id = IdeRemountWake.ArmIdPrefix
                 + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                 + "-" + Guid.NewGuid().ToString("N")[..6];

        IgniteArm arm;
        lock (Gate)
        {
            Arms.RemoveAll(a =>
                a.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");

            arm = new IgniteArm
            {
                Id = id,
                Event = "timer",
                Message = IdeIgniteChannel.ComposeRemountInitializedCharge(),
                ChargeMode = IdeRemountWake.ChargeMode,
                Task = IdeRemountWake.ArmTask,
                Once = true,
                LastOnce = false,
                OkOnly = true,
                SettleSeconds = 2,
                WaitSeconds = 90,
                DueUtc = now + TimeSpan.FromSeconds(dueSec),
                InRaw = $"{dueSec}s",
                Status = "armed",
                CreatedUtc = now,
                LastError = pending?.Reason is { Length: > 0 } r ? $"pending:{r}" : null
            };
            Arms.Add(arm);
            PersistUnlocked();
        }

        return Slim(arm);
    }

    /// <summary>
    /// Reclaim timer arms that are overdue or stuck in firing (killed mid-CDT).
    /// Same id — does not create a new arm. Returns reclaimed ids.
    /// </summary>
    internal static IReadOnlyList<string> ReclaimOverdue(TimeSpan? settle = null)
    {
        EnsureLoaded();
        var backoff = settle ?? TimeSpan.FromSeconds(3);
        if (backoff < TimeSpan.Zero) backoff = TimeSpan.Zero;
        var now = DateTimeOffset.UtcNow;
        var ids = new List<string>();
        lock (Gate)
        {
            var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Arms)
            {
                var stuckFiring = a.Status == "firing";
                // last_once already entered fire — latch awaiting (do not revive / multi-inject)
                if (stuckFiring && a.LastOnce && a.FiredUtc is not null)
                {
                    a.Status = "awaiting";
                    a.LastError = "reclaimed_last_once_firing";
                    Firing.TryRemove(a.Id, out _);
                    ids.Add(a.Id);
                    continue;
                }

                // once already entered fire (FiredUtc stamped) — do not revive mid-CDT kill (multi-inject)
                if (stuckFiring && a.Once && a.FiredUtc is not null)
                {
                    drop.Add(a.Id);
                    Firing.TryRemove(a.Id, out _);
                    continue;
                }

                var overdue = a.Event.Equals("timer", StringComparison.OrdinalIgnoreCase)
                              && a.DueUtc is { } d
                              && d <= now
                              && a.Status is "armed" or "firing";
                if (!overdue && !stuckFiring) continue;

                a.Status = "armed";
                a.LastError = stuckFiring && !overdue ? "reclaimed_stuck_firing" : "reclaimed_overdue";
                a.DueUtc = now + backoff;
                Firing.TryRemove(a.Id, out _);
                ids.Add(a.Id);
            }

            if (drop.Count > 0)
                Arms.RemoveAll(a => drop.Contains(a.Id));

            if (ids.Count > 0 || drop.Count > 0)
                PersistUnlocked();
        }

        return ids;
    }

    /// <summary>Test hook: mutate in-memory arm under Gate.</summary>
    internal static bool TryMutateForTests(string id, Action<IgniteArm> mutate)
    {
        EnsureLoaded();
        lock (Gate)
        {
            var a = Arms.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (a is null) return false;
            mutate(a);
            PersistUnlocked();
            return true;
        }
    }

}
