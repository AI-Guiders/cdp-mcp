#nullable enable
using System.Globalization;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Fail-closed latch: provider refusal after fire → explicit continuity state.</summary>
    internal static bool ShouldLatchAwaitingOnFireError(string? error) =>
        ShouldEnterProviderBlockedContinuity(error);

    /// <summary>tool-wake-* once arms — never requeue after busy; call usually already finished.</summary>
    internal static bool IsToolWakeArmId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith("tool-wake-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Remount / tool wake — must not be wiped by a later continuity timer re-arm.</summary>
    internal static bool IsSystemWakeArmId(string? id) =>
        IsToolWakeArmId(id)
        || (!string.IsNullOrWhiteSpace(id)
            && id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Harness event wakes (build/test/shell) — never superseded by a continuity timer.</summary>
    internal static bool IsEventTriggeredArm(string? eventName)
    {
        var e = NormalizeEvent(eventName);
        return e is "build_finished" or "test_finished" or "shell_finished";
    }

    /// <summary>Only plain armed continuity timers may be replaced by a later timer re-arm.</summary>
    internal static bool IsSupersedableContinuityWorkTimer(IgniteArm a) =>
        string.Equals(a.Event, "timer", StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Status, "armed", StringComparison.OrdinalIgnoreCase)
        && !IsSystemWakeArmId(a.Id)
        && !IsEventTriggeredArm(a.Event);

    /// <summary>Cancel CDT inject in flight (Disarm / call-complete ClearWakeArm).</summary>
    internal static void CancelInFlightFire(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (FireTokens.TryRemove(id, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            try { cts.Dispose(); } catch { /* ignore */ }
        }

        Firing.TryRemove(id, out _);
    }

    static void CancelAllInFlightFires()
    {
        foreach (var id in FireTokens.Keys.ToArray())
            CancelInFlightFire(id);
    }

    /// <summary>Test hook — attach a fire CTS as QueueFire would.</summary>
    internal static CancellationTokenSource AttachFireTokenForTests(string id)
    {
        var cts = new CancellationTokenSource();
        FireTokens[id] = cts;
        Firing[id] = 0;
        return cts;
    }

    static bool IsArmLive(string id)
    {
        lock (Gate)
            return Arms.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    static void QueueFire(IgniteArm arm, bool ok, string? pulse, string? detail)
    {
        if (!Firing.TryAdd(arm.Id, 0)) return;
        var cts = new CancellationTokenSource();
        FireTokens[arm.Id] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunFireAsync(arm, ok, pulse, detail, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                CleanupFireToken(arm.Id);
            }
        });
    }

    static async Task RunFireAsync(
        IgniteArm arm, bool ok, string? pulse, string? detail, CancellationToken ct)
    {
        try
        {
            SetStatus(arm.Id, "firing", null);
            if (arm.SettleSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(arm.SettleSeconds), ct).ConfigureAwait(false);

            // Disarmed while settling / before CDT — do not inject stale charge.
            if (!IsArmLive(arm.Id))
            {
                IdeFlightDataRecorder.RecordWake(
                    "wake_suppress", arm.Id, ToolFromWakeArm(arm), "disarmed_before_fire");
                return;
            }

            var msg = ComposeFireCharge(arm, ok, pulse, detail);
            MarkSendInvoked(arm.Id);

            // Last gate: Disarm may have raced after IsArmLive check.
            if (!IsArmLive(arm.Id))
            {
                IdeFlightDataRecorder.RecordWake(
                    "wake_suppress", arm.Id, ToolFromWakeArm(arm), "disarmed_before_cdt");
                return;
            }

            var result = await IdeIgniteChannel.FireAsync(
                arm.Port, msg, arm.Chat, arm.WaitSeconds, ct).ConfigureAwait(false);
            ApplyFireOutcome(arm, result);
        }
        catch (OperationCanceledException)
        {
            IdeFlightDataRecorder.RecordWake(
                "wake_cancel", arm.Id, ToolFromWakeArm(arm), "fire_token_cancelled");
            // Disarm already removed; do not resurrect.
        }
        catch (Exception ex)
        {
            IdeStageCycle.TryAppend("ignite.fire_fail", "ignite", ex.Message, arm.Id);
            if (ShouldKeepVisibleErrorOnFireFail(arm.Once, arm.LastOnce))
                SetStatus(arm.Id, "error", ex.Message);
            else
                Remove(arm.Id);
        }
    }

    static string ComposeFireCharge(IgniteArm arm, bool ok, string? pulse, string? detail) =>
        IsCustomChargeMode(arm.ChargeMode)
            ? IdeIgniteChannel.SanitizeComposerCharge(Expand(arm.Message, arm, ok, pulse, detail))
            : IsRemountChargeMode(arm.ChargeMode)
                ? IdeIgniteChannel.ComposeRemountInitializedCharge(
                    IdePressureChannel.TryPeekProjectRoot(),
                    IdeDomainPulse.FocusHintFromPlanLatch())
                : IdeIgniteChannel.ComposeArmFireCharge();

    static void MarkSendInvoked(string armId)
    {
        lock (Gate)
        {
            var live = Arms.FirstOrDefault(x => x.Id.Equals(armId, StringComparison.OrdinalIgnoreCase));
            if (live is null)
                return;
            live.SendInvokedUtc = DateTimeOffset.UtcNow;
            live.SendOk = null;
            live.SendError = null;
            PersistUnlocked();
        }
    }

    static void ApplyFireOutcome(IgniteArm arm, object? result)
    {
        var firedOk = result is { } && TryGetOk(result);
        RecordSendEvidence(arm.Id, firedOk, firedOk ? null : (TryGetError(result) ?? "fire_failed"));
        if (firedOk)
        {
            if (arm.LastOnce)
                SetStatus(arm.Id, "awaiting", null, fired: DateTimeOffset.UtcNow);
            else if (arm.Once)
                Remove(arm.Id);
            else
                SetStatus(arm.Id, "armed", null, fired: DateTimeOffset.UtcNow);
            return;
        }

        var err = TryGetError(result) ?? "fire_failed";
        IdeStageCycle.TryAppend(
            IdeStageCycle.MapIgniteError(err), "ignite", err, arm.Id);
        if (ShouldRequeueBusy(arm.Event, err) && !IsToolWakeArmId(arm.Id))
            RequeueAfterBusy(arm.Id, err, BusyBackoff(arm.WaitSeconds));
        else if (ShouldEnterProviderBlockedContinuity(err))
            EnterProviderBlockedContinuity(arm, TryGetDetail(result));
        else if (ShouldKeepVisibleErrorOnFireFail(arm.Once, arm.LastOnce))
            SetStatus(arm.Id, "error", err);
        else
            Remove(arm.Id); // plain once — hygiene/reclaim scrub zombies
    }

    static void CleanupFireToken(string armId)
    {
        Firing.TryRemove(armId, out _);
        if (!FireTokens.TryRemove(armId, out var token))
            return;
        try { token.Dispose(); } catch { /* ignore */ }
    }

    static string? ToolFromWakeArm(IgniteArm arm)
    {
        var task = arm.Task ?? "";
        const string prefix = "tool-watch:";
        if (task.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return task[prefix.Length..];
        return IsToolWakeArmId(arm.Id) ? arm.Id : null;
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

        CancelInFlightFire(id);
    }

    static bool IsCustomChargeMode(string? mode)
    {
        var m = (mode ?? "minimal").Trim().ToLowerInvariant();
        return m is "custom" or "expand" or "legacy";
    }

    static bool IsRemountChargeMode(string? mode) =>
        string.Equals(
            (mode ?? "").Trim(),
            IdeRemountWake.ChargeMode,
            StringComparison.OrdinalIgnoreCase);

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
