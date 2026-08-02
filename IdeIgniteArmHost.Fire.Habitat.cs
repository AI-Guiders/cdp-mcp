#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    const int HabitatIntercomChargeCap = 2000;

    /// <summary>
    /// Plain continuity timers only — remount/OOM/HILD/event wakes stay off habitat prefer.
    /// Remount + HILD escalate/away + OOM + tool-wake still Intercom-mirror (see MirrorTimerWakeToIntercom).
    /// </summary>
    internal static bool MayPreferHabitatOverComposer(IgniteArm arm) =>
        string.Equals(arm.Event, "timer", StringComparison.OrdinalIgnoreCase)
        && !IsSystemWakeArmId(arm.Id)
        && !IsEventTriggeredArm(arm.Event);

    /// <summary>
    /// Duplex partner (PF) actively in habitat — busy|composing after effective stale.
    /// Idle/stale/missing → not duplex-live (see ShouldPreferHabitatDelivery for autonomous spine).
    /// </summary>
    internal static bool IsHabitatPartnerLive(DateTimeOffset? nowUtc = null)
    {
        var doc = CideIntercomPresenceLatch.TryReadEffective(nowUtc);
        var pf = doc?.Pf;
        if (pf is null || string.IsNullOrWhiteSpace(pf.State))
            return false;
        return pf.State is CideIntercomPresenceLatch.StateBusy
            or CideIntercomPresenceLatch.StateComposing;
    }

    /// <summary>
    /// Prefer habitat when PF duplex live, or autonomous overnight (plain timers).
    /// Partner-mode (autonomous off) + idle PF → Composer fallthrough + Intercom mirror.
    /// </summary>
    internal static bool ShouldPreferHabitatDelivery(DateTimeOffset? nowUtc = null) =>
        IsHabitatPartnerLive(nowUtc) || IsAutonomousArmed();

    /// <summary>
    /// Duplex PF live: publish habitat wake + Intercom, skip CDT.
    /// Autonomous + idle PF: stamp habitat SSOT only, return null → Guest Autoi CDT fallthrough
    /// (Intercom via MirrorTimerWakeToIntercom). Skipping CDT overnight was ACC silent for guest Cursor.
    /// </summary>
    internal static object? TryDeliverHabitatWake(IgniteArm arm, string charge)
    {
        if (!MayPreferHabitatOverComposer(arm))
            return null;
        if (!ShouldPreferHabitatDelivery())
            return null;

        var duplex = IsHabitatPartnerLive();
        var detail = duplex ? "prefer_duplex" : "prefer_autonomous";

        var latch = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            IdeIgniteWakeLatch.ChannelHabitat,
            arm.Reason,
            arm.Task);
        if (latch is null)
            return null;

        IdeFlightDataRecorder.RecordWake(
            "wake_habitat", arm.Id, ToolFromWakeArm(arm), detail);

        // Guest Autoi spine: habitat SSOT without killing CDT inject when no duplex consumer.
        if (!duplex)
            return null;

        PublishHabitatIntercomCharge(charge);

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


    internal static bool MirrorTimerWakeToIntercom(IgniteArm arm, string charge)
    {
        string detail;
        if (IsRemountWakeArm(arm))
        {
            detail = "remount_intercom";
        }
        else if (IsHildEscalateWakeArm(arm))
        {
            // Lived: escalate CDT while Composer Stop → busy_timeout (parity remount).
            detail = "escalate_intercom";
        }
        else if (IsHildAwayWakeArm(arm))
        {
            // Lived: first human_away once + Composer Stop → no timer requeue → silent drop until escalate.
            detail = "hild_intercom";
        }
        else if (IsOomWakeArm(arm))
        {
            // Residual: OOM recover still Composer adapter; Glass needs charge when CDT waits Stop.
            detail = "oom_intercom";
        }
        else if (IsToolWakeArmId(arm.Id))
        {
            // Lived risk: tool-wake once + busy_timeout → no requeue → silent drop while Composer Stop.
            detail = "tool_intercom";
        }
        else
        {
            if (!MayPreferHabitatOverComposer(arm))
                return false;
            if (IsHabitatPartnerLive())
                return false;
            detail = "idle_pf_intercom";
        }

        var voiceBody = TruncateHabitatCharge(charge);
        var voice = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: voiceBody,
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: "AutoI",
            kind: "guest");
        if (voice is null)
            return false;

        IdeFlightDataRecorder.RecordWake(
            "wake_habitat_mirror", arm.Id, ToolFromWakeArm(arm), detail);
        return true;
    }

    /// <summary>remount-wake-* — Intercom mirror residual; not habitat prefer.</summary>
    internal static bool IsRemountWakeArm(IgniteArm arm) =>
        !string.IsNullOrWhiteSpace(arm.Id)
        && arm.Id.StartsWith(IdeRemountWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>hild-escalate-* — Intercom mirror residual (Composer Stop busy_timeout tooth).</summary>
    internal static bool IsHildEscalateWakeArm(IgniteArm arm) =>
        !string.IsNullOrWhiteSpace(arm.Id)
        && arm.Id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>hild-away / hild-away-* — first human_away once; Intercom mirror (not escalate).</summary>
    internal static bool IsHildAwayWakeArm(IgniteArm arm) =>
        !string.IsNullOrWhiteSpace(arm.Id)
        && (arm.Id.Equals(HildAwayArmId, StringComparison.OrdinalIgnoreCase)
            || arm.Id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>oom-wake-* — Intercom mirror residual (Composer Stop after recover).</summary>
    internal static bool IsOomWakeArm(IgniteArm arm) =>
        !string.IsNullOrWhiteSpace(arm.Id)
        && arm.Id.StartsWith(IdeOomWake.ArmIdPrefix, StringComparison.OrdinalIgnoreCase);



    /// <summary>Composer Stop/Queue — CDT cannot inject without wait/busy_timeout.</summary>
    internal static bool IsComposerBusyKind(string kind) =>
        string.Equals(kind, "stop", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "queue", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Skip CDT when Composer Stop/Queue **or** surface gone (no_composer/down).
    /// Voice/send/idle: false → CDT fallthrough. Sample fail (!ok) uses kind no_composer|down.
    /// </summary>
    internal static bool ShouldSkipCdtAfterIntercomMirror(bool sampleOk, string kind) =>
        !sampleOk
        || IsComposerBusyKind(kind)
        || string.Equals(kind, "no_composer", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "down", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Habitat-skip CDT when Composer unavailable — except Guest Autoi overnight + idle PF
    /// + Stop/Queue: that path stamped SSOT then consumed last_once without Composer inject
    /// (lived: leaf-wake habitat latch, arms=[], operator "выстрела нет"). Fall through to
    /// CDT wait / busy_timeout→requeue. Duplex / system wakes / composer_gone stay skip.
    /// </summary>
    internal static bool ShouldHabitatSkipWhenComposerUnavailable(
        IgniteArm arm, bool sampleOk, string kind, bool autonomousArmed, bool duplexLive)
    {
        if (!MayDeliverHabitatWhenComposerUnavailable(arm))
            return false;
        if (!ShouldSkipCdtAfterIntercomMirror(sampleOk, kind))
            return false;
        if (MayPreferHabitatOverComposer(arm)
            && autonomousArmed
            && !duplexLive
            && IsComposerBusyKind(kind))
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
        return IsRemountWakeArm(arm) ? $"remount_{suffix}"
            : IsHildEscalateWakeArm(arm) ? $"escalate_{suffix}"
            : IsHildAwayWakeArm(arm) ? $"hild_{suffix}"
            : IsOomWakeArm(arm) ? $"oom_{suffix}"
            : IsToolWakeArmId(arm.Id) ? $"tool_{suffix}"
            : $"idle_pf_{suffix}";
    }

    /// <summary>
    /// Composer Stop/Queue/gone: habitat deliver, skip CDT — no Intercom mirror required.
    /// Covers Voice Publish miss / mirror false → residual no_agent_composer thrash (0.5.527).
    /// Voice/idle Composer: null → CDT fallthrough.
    /// </summary>
    internal static async Task<object?> TryDeliverHabitatWhenComposerUnavailableAsync(
        IgniteArm arm, string charge, CancellationToken ct)
    {
        if (!MayDeliverHabitatWhenComposerUnavailable(arm))
            return null;

        var (ok, kind, _) = await IdeIgniteChannel.TrySampleComposerAsync(arm.Port, ct)
            .ConfigureAwait(false);
        if (!ShouldHabitatSkipWhenComposerUnavailable(
                arm, ok, kind, IsAutonomousArmed(), IsHabitatPartnerLive()))
            return null;

        var latch = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            IdeIgniteWakeLatch.ChannelHabitat,
            arm.Reason,
            arm.Task);
        if (latch is null)
            return null;

        // Parity prefer duplex: Glass Intercom needs charge when mirror miss + composer gone (0.5.529).
        PublishHabitatIntercomCharge(charge);

        var detail = HabitatComposerSkipDetail(arm, kind);
        IdeFlightDataRecorder.RecordWake(
            "wake_habitat", arm.Id, ToolFromWakeArm(arm), detail);

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
    internal static Task<object?> TryDeliverMirroredWhenComposerBusyAsync(
        IgniteArm arm, string charge, bool intercomMirrored, CancellationToken ct)
    {
        if (!intercomMirrored)
            return Task.FromResult<object?>(null);
        return TryDeliverHabitatWhenComposerUnavailableAsync(arm, charge, ct);
    }

    /// <summary>Best-effort Intercom voice for habitat wakes (prefer + composer-unavailable).</summary>
    static void PublishHabitatIntercomCharge(string charge)
    {
        var voiceBody = TruncateHabitatCharge(charge);
        _ = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: voiceBody,
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: "AutoI",
            kind: "guest");
    }

    static string TruncateHabitatCharge(string charge)
    {
        var t = charge.Trim();
        if (t.Length <= HabitatIntercomChargeCap)
            return t;
        return t[..HabitatIntercomChargeCap] + "\n…[truncated habitat wake]";
    }
}
