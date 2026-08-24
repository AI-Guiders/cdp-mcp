#nullable enable

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

    /// <summary>Remount / OOM / escalate / tool wake — must not be wiped by a later continuity timer re-arm.</summary>
    internal static bool IsSystemWakeArmId(string? id) =>
        IsToolWakeArmId(id)
        || (!string.IsNullOrWhiteSpace(id)
            && (id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                || id.Equals(HildAwayArmId, StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Harness event wakes (build/test/shell/hild) — never superseded by a continuity timer.</summary>
    internal static bool IsEventTriggeredArm(string? eventName)
    {
        var e = NormalizeEvent(eventName);
        return e is "build_finished" or "test_finished" or "shell_finished" or "human_away" or "peer_ship";
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
        using var tenantScope = EnterArmTenantScope(arm);
        try
        {
            // New ignition supersedes post-fire Connection Problems watch.
            IdeIgniteConnectionWatch.Stop();
            SetStatus(arm.Id, "firing", null);
            IdeTeethTape.Record(
                "wake_fire", armId: arm.Id, reason: arm.Reason, detail: arm.ChargeMode);
            if (arm.SettleSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(arm.SettleSeconds), ct).ConfigureAwait(false);

            // Disarmed while settling / before CDT — do not inject stale charge.
            if (!IsArmLive(arm.Id))
            {
                IdeFlightDataRecorder.RecordWake(
                    "wake_suppress", arm.Id, ToolFromWakeArm(arm), "disarmed_before_fire");
                return;
            }

            // LeafPlateau seed: board may already have next leaf mid-window — skip Guest Autoi thrash.
            if (TrySuppressAutonomousSeedBeforeDelivery(arm))
                return;

            var msg = ComposeFireCharge(arm, ok, pulse, detail);

            // Last gate: Disarm may have raced after IsArmLive check.
            if (!IsArmLive(arm.Id))
            {
                IdeFlightDataRecorder.RecordWake(
                    "wake_suppress", arm.Id, ToolFromWakeArm(arm), "disarmed_before_cdt");
                return;
            }

            // Habitat prefer — duplex skip CDT; autonomous stamps habitat SSOT then may fall through.
            var habitat = TryDeliverHabitatWake(arm, msg);
            if (habitat is not null)
            {
                MarkSendInvoked(arm.Id);
                ApplyFireOutcome(arm, habitat);
                return;
            }

            // Idle PF / remount / HILD escalate / OOM / tool-wake: mirror charge to Intercom; Composer fallthrough below.
            var mirrored = MirrorTimerWakeToIntercom(arm, msg);

            // Intercom mirrored + Composer Stop/Queue/gone: habitat already has charge — skip CDT busy wait/requeue.
            var mirroredBusy = await TryDeliverMirroredWhenComposerBusyAsync(arm, msg, mirrored, ct)
                .ConfigureAwait(false);
            if (mirroredBusy is not null)
            {
                MarkSendInvoked(arm.Id);
                ApplyFireOutcome(arm, mirroredBusy);
                return;
            }

            // Mirror miss / Voice Publish fail + Composer unavailable: still habitat — skip dead CDT.
            var unavailable = await TryDeliverHabitatWhenComposerUnavailableAsync(arm, msg, ct)
                .ConfigureAwait(false);
            if (unavailable is not null)
            {
                MarkSendInvoked(arm.Id);
                ApplyFireOutcome(arm, unavailable);
                return;
            }

            // Provider dispatch: OpenCode native wake when configured; else Cursor Composer (default).
            if (IdeIgniteChannel.IsOpencodeConfigured())
            {
                MarkSendInvoked(arm.Id);
                var oc = await IdeIgniteChannel.FireToOpencodeAsync(msg, ct).ConfigureAwait(false);
                ApplyFireOutcome(arm, oc);
                return;
            }

            // Composer adapter path — keep habitat SSOT if autonomous stamp already wrote it.
            if (!IdeIgniteWakeLatch.IsHabitatLatchForArm(arm.Id))
            {
                _ = IdeIgniteWakeLatch.Publish(
                    arm.Id, msg, IdeIgniteWakeLatch.ChannelComposer, arm.Reason, arm.Task);
            }

            // Mark send only when CDT inject starts — not during wait-idle (Stop).
            MarkSendInvoked(arm.Id);
            var chat = IdeIgniteArmHost.ResolveChatFromTenantLatch(arm.TenantWire, arm.ConversationId, arm.Chat);
            var result = await IdeIgniteChannel.FireAsync(
                arm.Port, msg, chat, arm.WaitSeconds, ct).ConfigureAwait(false);
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
            IdeTeethTape.Record(
                "wake_send",
                armId: armId,
                reason: live.Reason,
                detail: "invoked");
        }
    }

    /// <summary>
    /// Insert landed and Composer already Stop — peer/zombie click or auto-send.
    /// Treat as delivery so once-arms do not thrash (dogfood escalate became_stop storm).
    /// </summary>
    internal static bool IsSoftDeliveredError(string? error) =>
        string.Equals(error, "became_stop", StringComparison.Ordinal);

    static void ApplyFireOutcome(IgniteArm arm, object? result)
    {
        var rawErr = result is { } && TryGetOk(result) ? null : (TryGetError(result) ?? "fire_failed");
        var softStop = IsSoftDeliveredError(rawErr);
        var firedOk = rawErr is null || softStop;
        var err = firedOk ? null : rawErr;
        var submit = TryGetStringProp(result, "submit_kind_after")
                     ?? TryGetStringProp(result, "submit_kind");
        RecordSendEvidence(arm.Id, firedOk, err);
        IdeTeethTape.Record(
            "wake_send",
            armId: arm.Id,
            reason: arm.Reason,
            detail: softStop ? "ok_soft_stop" : firedOk ? "ok" : (err ?? "fail"),
            submitKind: submit ?? (softStop ? "stop" : null));
        if (firedOk)
        {
            // Habitat duplex / citizen delivery never touched Composer — no CDT Connection Problems watch.
            if (!IsHabitatSubmitKind(submit))
                IdeIgniteConnectionWatch.Start(arm.Port);
            if (arm.LastOnce)
            {
                // ACC: under autonomous, last_once insurance delivered ≠ invent-ban awaiting_partner
                // (habitat skip or CDT). Agent continues; re-ARM is end-of-turn. Seed if path empty.
                if (ShouldLatchAwaitingPartnerAfterSuccessfulFire(arm.LastOnce, IsAutonomousArmed()))
                    SetStatus(arm.Id, "awaiting", null, fired: DateTimeOffset.UtcNow);
                else
                {
                    Remove(arm.Id);
                    if (!HasLiveWakePathUnlocked())
                        _ = AutonomousContinue("last_once_delivered_autonomous");
                }
            }
            else if (arm.Once)
                Remove(arm.Id);
            else
                SetStatus(arm.Id, "armed", null, fired: DateTimeOffset.UtcNow);
            return;
        }

        err ??= "fire_failed";
        IdeStageCycle.TryAppend(
            IdeStageCycle.MapIgniteError(err), "ignite", err, arm.Id);
        if (ShouldRequeueBusy(arm.Event, err) && !IsToolWakeArmId(arm.Id))
        {
            // First busy under Composer Stop — Face tip once (requeue keeps LastError set).
            if (IsIntercomVoiceCannonArmId(arm.Id) && arm.LastError is null)
                PublishVoiceCannonDeliveryFailFace(arm, err);
            RequeueAfterBusy(arm.Id, err, BusyBackoff(arm.WaitSeconds));
        }
        else if (ShouldEnterProviderBlockedContinuity(err))
            EnterProviderBlockedContinuity(arm, TryGetDetail(result));
        else if (ShouldKeepVisibleErrorOnFireFail(arm.Once, arm.LastOnce))
            SetStatus(arm.Id, "error", err);
        else
        {
            // Lived SoftFL: once voice cannon marked fired before CDT — silent Remove under
            // Composer Stop/non-requeue left @Kir with no retry + no Face tip.
            if (IsIntercomVoiceCannonArmId(arm.Id))
            {
                PublishVoiceCannonDeliveryFailFace(arm, err);
                ClearVoiceCannonFiredClaim(arm.Id);
            }
            Remove(arm.Id); // plain once — hygiene/reclaim scrub zombies
        }
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
}
