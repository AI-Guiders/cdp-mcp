#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    const int HabitatIntercomChargeCap = 2000;

    /// <summary>
    /// Plain continuity timers only — remount/OOM/HILD/event wakes stay off habitat prefer.
    /// Remount + HILD escalate/away + OOM + tool-wake still Intercom-mirror (see MirrorTimerWakeToIntercom).
    /// Intercom voice cannon (intercom-pf-*) must stay Composer/CDT: external guest @Kir/@guest
    /// must not silent-steal to habitat when Sierra·PF is duplex busy (lived 2026-08-07).
    /// </summary>
    internal static bool MayPreferHabitatOverComposer(IgniteArm arm) =>
        string.Equals(arm.Event, "timer", StringComparison.OrdinalIgnoreCase)
        && !IsSystemWakeArmId(arm.Id)
        && !IsIntercomVoiceCannonArmId(arm.Id)
        && !IsEventTriggeredArm(arm.Event);

    /// <summary>Human→PF Intercom cannon arms — Guest Autoi CDT only (not habitat prefer).</summary>
    internal static bool IsIntercomVoiceCannonArmId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith(IntercomVoiceCannonState.ArmIdPrefix, StringComparison.OrdinalIgnoreCase);
    static string? VoiceCannonMsgIdFromArmId(string? armId)
    {
        if (string.IsNullOrWhiteSpace(armId))
            return null;
        if (!armId.StartsWith(IntercomVoiceCannonState.ArmIdPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var id = armId[IntercomVoiceCannonState.ArmIdPrefix.Length..].Trim();
        return id.Length == 0 ? null : id;
    }

    static void ClearVoiceCannonFiredClaim(string armId)
    {
        var msgId = VoiceCannonMsgIdFromArmId(armId);
        if (msgId is null)
            return;
        _ = IntercomVoiceCannonState.TryClearFired(msgId);
    }

    /// <summary>Glass Face Radio — Composer Stop/busy must not look like @Kir swallowed.</summary>
    static void PublishVoiceCannonDeliveryFailFace(IgniteArm arm, string err)
    {
        if (!IsPrimaryAutoiSeat())
            return;
        var tip = string.Equals(err, "busy_timeout", StringComparison.Ordinal)
            ? "Radio · Composer Stop · @Kir wake pending (пушка ждёт Voice)"
            : $"Radio · @Kir wake fail · {err}";
        _ = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: tip,
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: "AutoI",
            kind: "guest",
            channel: "radio");
        IdeFlightDataRecorder.RecordWake(
            "wake_cannon_face",
            arm.Id,
            tool: null,
            detail: err);
    }


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

    internal static bool IsHabitatSubmitKind(string? submit) =>
        string.Equals(submit, "habitat", StringComparison.OrdinalIgnoreCase)
        || string.Equals(submit, "citizen", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prefer habitat when PF duplex live, or autonomous overnight (plain timers).
    /// Partner-mode (autonomous off) + idle PF → Composer fallthrough + Intercom mirror.
    /// </summary>
    internal static bool ShouldPreferHabitatDelivery(DateTimeOffset? nowUtc = null) =>
        IsHabitatPartnerLive(nowUtc) || IsAutonomousArmed();

    /// <summary>
    /// Duplex PF live: publish habitat wake + Intercom, skip CDT.
    /// Autonomous + idle PF: stamp habitat SSOT only, return null → Guest Autoi CDT→Composer
    /// (Intercom via MirrorTimerWakeToIntercom). Citizen consume is NOT here — while Cursor
    /// Composer is the live host seat, prefer_citizen would silent-steal the gun (lived:
    /// operator «пушка не выстрелила»). Citizen eats only when Composer unavailable
    /// (TryDeliverHabitatWhenComposerUnavailableAsync).
    /// </summary>
    internal static object? TryDeliverHabitatWake(IgniteArm arm, string charge)
    {
        if (!MayPreferHabitatOverComposer(arm))
            return null;
        if (!ShouldPreferHabitatDelivery())
            return null;

        var duplex = IsHabitatPartnerLive();

        // Dual Autoi: debug twin must not FDR/Intercom-double the live seat (lived ~174 wake_habitat*/h).
        if (!duplex && !IsPrimaryAutoiSeat())
            return null;

        var latch = IdeIgniteWakeLatch.Publish(
            arm.Id,
            charge,
            IdeIgniteWakeLatch.ChannelHabitat,
            arm.Reason,
            arm.Task);
        if (latch is null)
            return null;

        if (duplex)
        {
            IdeFlightDataRecorder.RecordWake(
                "wake_habitat", arm.Id, ToolFromWakeArm(arm), "prefer_duplex");
            if (IsPrimaryAutoiSeat())
                PublishHabitatIntercomCharge(arm, charge);
            return HabitatWakeResult(arm.Id, "prefer_duplex", submitKind: "habitat");
        }

        // Guest Autoi / Cursor host spine: habitat SSOT without killing CDT inject.
        IdeFlightDataRecorder.RecordWake(
            "wake_habitat", arm.Id, ToolFromWakeArm(arm), "prefer_autonomous");
        return null;
    }


    static object HabitatWakeResult(string armId, string detail, string submitKind) =>
        new
        {
            schema = "ignite/v0",
            ok = true,
            op = "send",
            submit_kind = submitKind,
            submit_kind_after = submitKind,
            channel = IdeIgniteWakeLatch.ChannelHabitat,
            arm_id = armId,
            detail
        };

    static void PublishCitizenWakeIntercom(IgniteArm arm, string body)
    {
        // Lived: remount/system prefer_citizen painted Autoi remount as kind=citizen —
        // stomped Glass Radio + stole Citizen Who. System wakes → guest Radio only.
        if (IsSystemWakeArmId(arm.Id))
        {
            PublishHabitatIntercomCharge(arm, body);
            return;
        }

        // Glass human face: Radio pointers — never TruncateCharge SA wall as Citizen.
        var voiceBody = FormatCitizenWakeIntercom(arm, body);
        if (LooksLikeHabitatRadioPointer(voiceBody))
        {
            PublishHabitatIntercomCharge(arm, voiceBody);
            return;
        }

        _ = CideIntercomVoiceLatch.Publish(
            fromSeat: CideIntercomVoiceLatch.SeatPf,
            toSeat: CideIntercomVoiceLatch.SeatPm,
            body: voiceBody,
            origin: CideIntercomVoiceLatch.OriginAgent,
            name: CideIntercomVoiceLatch.DefaultNameCitizen,
            kind: CideIntercomVoiceLatch.KindCitizen);
    }

    /// <summary>Collapsed Autoi Radio face (I6) — must stay guest, never Citizen Who.</summary>
    internal static bool LooksLikeHabitatRadioPointer(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;
        var t = body.TrimStart();
        if (t.StartsWith("Autoi ", StringComparison.OrdinalIgnoreCase)
            && t.Contains('\u00B7'))
            return true;
        return t.Contains("PFD.NEXT", StringComparison.Ordinal);
    }

    /// <summary>
    /// prefer_citizen Intercom: short prose OK; charge/SA-instrument walls → Radio (I6).
    /// </summary>
    internal static string FormatCitizenWakeIntercom(IgniteArm arm, string body)
    {
        var t = (body ?? "").Trim();
        if (t.Length == 0)
            return FormatHabitatIntercomRadio(arm, "");

        if (LooksLikeComposerChargeWall(t) || CitizenIntercomHumanSurface.LooksLikeSaInstrumentWall(t))
            return FormatHabitatIntercomRadio(arm, t);

        var clean = CitizenIntercomHumanSurface.StripWire(t);
        if (LooksLikeComposerChargeWall(clean)
            || CitizenIntercomHumanSurface.LooksLikeSaInstrumentWall(clean)
            || clean.Length > 480)
            return FormatHabitatIntercomRadio(arm, clean.Length > 0 ? clean : t);

        return clean;
    }


    internal static bool MirrorTimerWakeToIntercom(IgniteArm arm, string charge)
    {
        string detail;
        if (IsRemountWakeArm(arm))
        {
            // Lived Face thrash: remount Autoi tips mid-Sierra Turn (presence=busy).
            // CDT→Composer still ok; mute Radio while Face Who is working.
            if (IsHabitatPartnerLive())
                return false;
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
            if (!IsPrimaryAutoiSeat())
                return false;
            detail = "idle_pf_intercom";
        }

        // Dual Autoi (cdp + cdp-debug): one Intercom publish per wake family window.
        if (!TryClaimSharedWakeMirror(MirrorClaimKey(arm)))
            return false;

        var voiceBody = FormatHabitatIntercomRadio(arm, charge);
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

}
