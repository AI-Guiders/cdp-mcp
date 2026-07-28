#nullable enable
using System.Globalization;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Fail-closed latch: provider refusal after fire → explicit continuity state.</summary>
    internal static bool ShouldLatchAwaitingOnFireError(string? error) =>
        ShouldEnterProviderBlockedContinuity(error);

    static void QueueFire(IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        if (!Firing.TryAdd(arm.Id, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                SetStatus(arm.Id, "firing", null);
                if (arm.SettleSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(arm.SettleSeconds)).ConfigureAwait(false);

                var msg = IsCustomChargeMode(arm.ChargeMode)
                    ? IdeIgniteChannel.SanitizeComposerCharge(Expand(arm.Message, arm, ok, pulse, detail))
                    : IdeIgniteChannel.ComposeArmFireCharge();
                var result = await IdeIgniteChannel.FireAsync(
                    arm.Port, msg, arm.Chat, arm.WaitSeconds, CancellationToken.None).ConfigureAwait(false);

                var firedOk = result is { } && TryGetOk(result);
                if (firedOk)
                {
                    if (arm.LastOnce)
                        SetStatus(arm.Id, "awaiting", null, fired: DateTimeOffset.UtcNow);
                    else if (arm.Once)
                        Remove(arm.Id);
                    else
                        SetStatus(arm.Id, "armed", null, fired: DateTimeOffset.UtcNow);
                }
                else
                {
                    var err = TryGetError(result) ?? "fire_failed";
                    IdeStageCycle.TryAppend(
                        IdeStageCycle.MapIgniteError(err), "ignite", err, arm.Id);
                    if (ShouldRequeueBusy(arm.Event, err))
                        RequeueAfterBusy(arm.Id, err, BusyBackoff(arm.WaitSeconds));
                    else if (ShouldEnterProviderBlockedContinuity(err))
                        EnterProviderBlockedContinuity(arm, TryGetDetail(result));
                    else if (ShouldKeepVisibleErrorOnFireFail(arm.Once, arm.LastOnce))
                        SetStatus(arm.Id, "error", err);
                    else
                        Remove(arm.Id); // plain once — hygiene/reclaim scrub zombies
                }
            }
            catch (Exception ex)
            {
                IdeStageCycle.TryAppend("ignite.fire_fail", "ignite", ex.Message, arm.Id);
                if (ShouldKeepVisibleErrorOnFireFail(arm.Once, arm.LastOnce))
                    SetStatus(arm.Id, "error", ex.Message);
                else
                    Remove(arm.Id);
            }
            finally
            {
                Firing.TryRemove(arm.Id, out _);
            }
        });
    }

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

    /// <summary>Timer arms: Cursor still Stop/Queue → keep armed, push DueUtc (do not kill).</summary>
    internal static bool ShouldRequeueBusy(string eventName, string? error) =>
        string.Equals(error, "busy_timeout", StringComparison.Ordinal)
        && string.Equals(eventName, "timer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// last_once miss (chat_not_found etc.) must stay visible as error — silent Remove wiped ~2h of continuity.
    /// plain once still Remove (hygiene).
    /// </summary>
    internal static bool ShouldKeepVisibleErrorOnFireFail(bool once, bool lastOnce) =>
        lastOnce || !once;

    /// <summary>Backoff after busy_timeout: 15–60s, scales with wait_seconds.</summary>
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
        }
    }

    static void Remove(string id)
    {
        lock (Gate)
        {
            Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            PersistUnlocked();
        }
    }

    static bool IsCustomChargeMode(string? mode)
    {
        var m = (mode ?? "minimal").Trim().ToLowerInvariant();
        return m is "custom" or "expand" or "legacy";
    }

    static string Expand(string template, IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        var t = template
            .Replace("{event}", IdeIgniteChannel.EventTokenForCharge(arm.Event), StringComparison.OrdinalIgnoreCase)
            .Replace("{task}", arm.Task ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{ok}", ok ? "ok" : "fail", StringComparison.OrdinalIgnoreCase)
            .Replace("{pulse}", pulse ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{detail}", detail ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", arm.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{when}", DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        return t;
    }
}
