#nullable enable

namespace CdpMcp;

/// <summary>
/// OOM wake arm scheduling — stays on ArmHost because it mutates Arms/Gate.
/// Probe loop lives in <see cref="IdeIgniteOomWatch"/>.
/// </summary>
internal static partial class IdeIgniteArmHost
{
    /// <summary>Arm one-shot timer charge_mode=oom (system wake — not superseded).</summary>
    internal static object? TryScheduleOomWake()
    {
        // Dual-seat: only one process schedules within WakeCooldown.
        if (!IdeOomCrossProcessClaim.TryClaimSchedule(IdeOomWake.WakeCooldown))
            return null;

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

            // After OOM remount Composer is empty → HILD races with minimal charge and
            // steals the wake; drop pending HILD so agent sees reason=oom instead.
            Arms.RemoveAll(a =>
                a.Id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                && a.Status is "armed" or "firing");

            arm = new IgniteArm
            {
                Id = id,
                Event = "timer",
                Message = IdeIgniteChannel.ComposeOomWakeCharge(),
                ChargeMode = IdeOomWake.ChargeMode,
                Task = IdeOomWake.ArmTask,
                Reason = IdeOomWake.Reason,
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
