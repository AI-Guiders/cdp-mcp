#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    const int HabitatIntercomChargeCap = 2000;

    /// <summary>
    /// Plain continuity timers only — remount/OOM/HILD/event wakes stay off habitat prefer.
    /// Remount + HILD escalate still Intercom-mirror (see MirrorTimerWakeToIntercom).
    /// </summary>
    internal static bool MayPreferHabitatOverComposer(IgniteArm arm) =>
        string.Equals(arm.Event, "timer", StringComparison.OrdinalIgnoreCase)
        && !IsSystemWakeArmId(arm.Id)
        && !IsEventTriggeredArm(arm.Event);

    /// <summary>
    /// Duplex partner (PF) actively in habitat — busy|composing after effective stale.
    /// Idle/stale/missing → Composer adapter remains delivery; Intercom may still mirror (see MirrorTimerWakeToIntercom).
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
    /// When duplex partner live: publish wake latch + intercom, skip CDT inject.
    /// Returns fire-shaped ok result, or null to fall through to Composer.
    /// </summary>
    internal static object? TryDeliverHabitatWake(IgniteArm arm, string charge)
    {
        if (!MayPreferHabitatOverComposer(arm))
            return null;
        if (!IsHabitatPartnerLive())
            return null;

        var latch = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            IdeIgniteWakeLatch.ChannelHabitat,
            arm.Reason,
            arm.Task);
        if (latch is null)
            return null;

        var voiceBody = TruncateHabitatCharge(charge);
        _ = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: voiceBody,
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: "AutoI",
            kind: "guest");

        IdeFlightDataRecorder.RecordWake(
            "wake_habitat", arm.Id, ToolFromWakeArm(arm), "prefer_duplex");

        return new
        {
            schema = "ignite/v0",
            ok = true,
            op = "send",
            submit_kind = "habitat",
            submit_kind_after = "habitat",
            channel = IdeIgniteWakeLatch.ChannelHabitat,
            arm_id = arm.Id
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



    /// <summary>Composer Stop/Queue — CDT cannot inject without wait/busy_timeout.</summary>
    internal static bool IsComposerBusyKind(string kind) =>
        string.Equals(kind, "stop", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "queue", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// After Intercom mirror (remount / escalate / idle-PF) + Composer Stop/Queue: habitat deliver, skip CDT.
    /// Avoids busy_timeout → requeue → mid-flight Composer paste while PF is working.
    /// Voice/idle Composer: null → CDT fallthrough (overnight / idle wakes still reach Composer).
    /// </summary>
    internal static async Task<object?> TryDeliverMirroredWhenComposerBusyAsync(
        IgniteArm arm, string charge, bool intercomMirrored, CancellationToken ct)
    {
        if (!intercomMirrored)
            return null;

        var (ok, kind, _) = await IdeIgniteChannel.TrySampleComposerAsync(arm.Port, ct)
            .ConfigureAwait(false);
        if (!ok || !IsComposerBusyKind(kind))
            return null;

        var latch = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            IdeIgniteWakeLatch.ChannelHabitat,
            arm.Reason,
            arm.Task);
        if (latch is null)
            return null;

        var detail = IsRemountWakeArm(arm) ? "remount_composer_busy"
            : IsHildEscalateWakeArm(arm) ? "escalate_composer_busy"
            : "idle_pf_composer_busy";
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

    static string TruncateHabitatCharge(string charge)
    {
        var t = charge.Trim();
        if (t.Length <= HabitatIntercomChargeCap)
            return t;
        return t[..HabitatIntercomChargeCap] + "\n…[truncated habitat wake]";
    }
}
