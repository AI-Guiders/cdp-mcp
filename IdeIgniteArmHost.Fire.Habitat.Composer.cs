namespace CdpMcp;
internal static partial class IdeIgniteArmHost
{
    /// <summary>remount-wake-* — Intercom mirror residual; not habitat prefer.</summary>
    internal static bool IsRemountWakeArm(IgniteArm arm) => !string.IsNullOrWhiteSpace(arm.Id) && arm.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase);
    /// <summary>hild-escalate-* — Intercom mirror residual (Composer Stop busy_timeout tooth).</summary>
    internal static bool IsHildEscalateWakeArm(IgniteArm arm) => !string.IsNullOrWhiteSpace(arm.Id) && arm.Id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase);
    /// <summary>hild-away / hild-away-* — first human_away once; Intercom mirror (not escalate).</summary>
    internal static bool IsHildAwayWakeArm(IgniteArm arm) => !string.IsNullOrWhiteSpace(arm.Id) && (arm.Id.Equals(HildAwayArmId, StringComparison.OrdinalIgnoreCase) || arm.Id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase));
    /// <summary>oom-wake-* — Intercom mirror residual (Composer Stop after recover).</summary>
    internal static bool IsOomWakeArm(IgniteArm arm) => !string.IsNullOrWhiteSpace(arm.Id) && arm.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase);
    /// <summary>Composer Stop/Queue — CDT cannot inject without wait/busy_timeout.</summary>
    internal static bool IsComposerBusyKind(string kind) => string.Equals(kind, "stop", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "queue", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Skip CDT when Composer Stop/Queue **or** surface gone (no_composer/down).
    /// Voice/send/idle: false → CDT fallthrough. Sample fail (!ok) uses kind no_composer|down.
    /// </summary>
    internal static bool ShouldSkipCdtAfterIntercomMirror(bool sampleOk, string kind) => !sampleOk || IsComposerBusyKind(kind) || string.Equals(kind, "no_composer", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "down", StringComparison.OrdinalIgnoreCase);
    /// <summary>
    /// Habitat-skip CDT when Composer unavailable — except Guest Autoi overnight + idle PF
    /// + Stop/Queue: that path stamped SSOT then consumed last_once without Composer inject
    /// (lived: leaf-wake habitat latch, arms=[], operator "выстрела нет"). Fall through to
    /// CDT wait / busy_timeout→requeue. Duplex / system wakes / composer_gone stay skip.
    /// </summary>
    internal static bool ShouldHabitatSkipWhenComposerUnavailable(IgniteArm arm, bool sampleOk, string kind, bool autonomousArmed, bool duplexLive)
    {
        if (!MayDeliverHabitatWhenComposerUnavailable(arm))
            return false;
        if (!ShouldSkipCdtAfterIntercomMirror(sampleOk, kind))
            return false;
        if (MayPreferHabitatOverComposer(arm) && autonomousArmed && !duplexLive && IsComposerBusyKind(kind))
            return false;
        return true;
    }

    /// <summary>
    /// Continuity wakes that may habitat-skip CDT when Composer unavailable.
    /// build/test/shell: intentional no mirror / no habitat (noise).
    /// </summary>
    internal static bool MayDeliverHabitatWhenComposerUnavailable(IgniteArm arm)
    {
        var e = NormalizeEvent(arm.Event);
        if (e is "build_finished" or "test_finished" or "shell_finished")
            return false;
        return MayPreferHabitatOverComposer(arm) || IsSystemWakeArmId(arm.Id);
    }

    static string HabitatComposerSkipDetail(IgniteArm arm, string kind)
    {
        var suffix = IsComposerBusyKind(kind) ? "composer_busy" : "composer_gone";
        return IsRemountWakeArm(arm) ? $"remount_{suffix}" : IsHildEscalateWakeArm(arm) ? $"escalate_{suffix}" : IsHildAwayWakeArm(arm) ? $"hild_{suffix}" : IsOomWakeArm(arm) ? $"oom_{suffix}" : IsToolWakeArmId(arm.Id) ? $"tool_{suffix}" : $"idle_pf_{suffix}";
    }

    /// <summary>
    /// Composer Stop/Queue/gone: habitat deliver, skip CDT — no Intercom mirror required.
    /// When invite ready: citizen Turn consumes (prefer_citizen) — Composer host gone only.
    /// Covers Voice Publish miss / mirror false → residual no_agent_composer thrash (0.5.527).
    /// Voice/idle Composer: null → CDT fallthrough (do not steal Cursor gun).
    /// </summary>
    internal static async Task<object?> TryDeliverHabitatWhenComposerUnavailableAsync(IgniteArm arm, string charge, CancellationToken ct)
    {
        if (!MayDeliverHabitatWhenComposerUnavailable(arm))
            return null;
        var(ok, kind, _) = await IdeIgniteChannel.TrySampleComposerAsync(arm.Port, ct).ConfigureAwait(false);
        if (!ShouldHabitatSkipWhenComposerUnavailable(arm, ok, kind, IsAutonomousArmed(), IsHabitatPartnerLive()))
            return null;
        var latch = IdeIgniteWakeLatch.Publish(arm.Id, charge, IdeIgniteWakeLatch.ChannelHabitat, arm.Reason, arm.Task);
        if (latch is null)
            return null;
        // Composer gone/down: citizen can own the wake without stealing Cursor Composer.
        if (IdeCitizenChannel.TryDeliverAutoiWake(charge, out var reply))
        {
            // Multi-principal: citizen Autoi owns Sierra lane — tip Кир stays Cursor Who.
            TryApplyCitizenFocusLane();
            IdeFlightDataRecorder.RecordWake("wake_habitat", arm.Id, ToolFromWakeArm(arm), "prefer_citizen");
            PublishCitizenWakeIntercom(arm, reply ?? charge);
            return HabitatWakeResult(arm.Id, "prefer_citizen", submitKind: "citizen");
        }

        // Parity prefer duplex: Glass Intercom needs charge when mirror miss + composer gone (0.5.529).
        PublishHabitatIntercomCharge(charge);
        var detail = HabitatComposerSkipDetail(arm, kind);
        IdeFlightDataRecorder.RecordWake("wake_habitat", arm.Id, ToolFromWakeArm(arm), detail);
        return new
        {
            schema = "ignite/v0",
            ok = true,
            op = "send",
            submit_kind = "habitat",
            submit_kind_after = "habitat",
            channel = IdeIgniteWakeLatch.ChannelHabitat,
            arm_id = arm.Id,
            detail
        };
    }

    /// <summary>
    /// After Intercom mirror + Composer unavailable: thin wrapper → shared habitat skip.
    /// </summary>
    internal static Task<object?> TryDeliverMirroredWhenComposerBusyAsync(IgniteArm arm, string charge, bool intercomMirrored, CancellationToken ct)
    {
        if (!intercomMirrored)
            return Task.FromResult<object?>(null);
        return TryDeliverHabitatWhenComposerUnavailableAsync(arm, charge, ct);
    }

    /// <summary>Best-effort Intercom voice for habitat wakes (prefer + composer-unavailable).</summary>
    static void PublishHabitatIntercomCharge(string charge) => PublishHabitatIntercomCharge(arm: null, charge);
    static void PublishHabitatIntercomCharge(IgniteArm? arm, string charge)
    {
        if (!IsPrimaryAutoiSeat())
            return;
        // Remount + Face busy → no Autoi Radio spam (parity MirrorTimerWakeToIntercom).
        if (arm is not null && IsRemountWakeArm(arm) && IsHabitatPartnerLive())
            return;
        if (arm is not null && !TryClaimSharedWakeMirror(MirrorClaimKey(arm)))
            return;
        var voiceBody = FormatHabitatIntercomRadio(arm, charge);
        _ = CideIntercomVoiceLatch.Publish(fromSeat: CideIntercomVoiceLatch.SeatPf, toSeat: CideIntercomVoiceLatch.SeatPm, body: voiceBody, origin: CideIntercomVoiceLatch.OriginAgent, name: "AutoI", kind: "guest");
    }

    static string TruncateHabitatCharge(string charge)
    {
        var t = charge.Trim();
        if (t.Length <= HabitatIntercomChargeCap)
            return t;
        return t[..HabitatIntercomChargeCap] + "\n…[truncated habitat wake]";
    }
}