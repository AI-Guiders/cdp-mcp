#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    const int HabitatIntercomChargeCap = 2000;

    /// <summary>
    /// Plain continuity timers only — remount/OOM/HILD/event wakes stay on CDT adapter.
    /// </summary>
    internal static bool MayPreferHabitatOverComposer(IgniteArm arm) =>
        string.Equals(arm.Event, "timer", StringComparison.OrdinalIgnoreCase)
        && !IsSystemWakeArmId(arm.Id)
        && !IsEventTriggeredArm(arm.Event);

    /// <summary>
    /// Duplex partner (PF) actively in habitat — busy|composing after effective stale.
    /// Idle/stale/missing → Composer adapter remains sole delivery.
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

    static string TruncateHabitatCharge(string charge)
    {
        var t = charge.Trim();
        if (t.Length <= HabitatIntercomChargeCap)
            return t;
        return t[..HabitatIntercomChargeCap] + "\n…[truncated habitat wake]";
    }
}
