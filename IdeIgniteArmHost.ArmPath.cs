#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    /// <summary>Skip re-arm when provider-blocked or last_once latch holds (unless force).</summary>
    static object? TrySkipReArmWhenLatched(IgniteArm arm, bool force)
    {
        if (force)
            return null;

        var blockedOnly = Arms.FirstOrDefault(a => a.Status == ProviderBlockedStatus);
        if (blockedOnly is not null)
        {
            return new
            {
                schema = IdeIgniteChannel.Schema,
                ok = true,
                op = "arm",
                skipped = true,
                error = NewThreadRequiredError,
                continuity = ProviderBlockedStatus,
                pulse = $"ignite · {ProviderBlockedStatus} · skip re-arm · {blockedOnly.Id}",
                arm = Slim(blockedOnly),
                arms = SceneSliceUnlocked(),
                explain = ExplainCardObject(IdeExplainability.New(
                    "ignite.continuity",
                    ProviderBlockedStatus,
                    "previous last_once fire ended provider-blocked on this chat",
                    "open new chat")),
                hint = "Provider blocked after fire — new chat required for PF. op=resume after handoff; force=true to replace."
            };
        }

        var latched = Arms.Where(a => a.Status is "awaiting" or ProviderBlockedStatus).ToList();
        if (!arm.LastOnce || latched.Count == 0)
            return null;

        var latch = latched[0];
        var latchErr = IsProviderBlockedStatus(latch.Status)
            ? NewThreadRequiredError
            : "awaiting_operator";
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "arm",
            skipped = true,
            error = latchErr,
            continuity = IsProviderBlockedStatus(latch.Status) ? ProviderBlockedStatus : "awaiting_operator",
            pulse = IsProviderBlockedStatus(latch.Status)
                ? $"ignite · {ProviderBlockedStatus} · skip re-arm · {latch.Id}"
                : $"ignite · awaiting · skip re-arm · {latch.Id}",
            arm = Slim(latch),
            arms = SceneSliceUnlocked(),
            explain = ExplainCardObject(IsProviderBlockedStatus(latch.Status)
                ? IdeExplainability.New(
                    "ignite.continuity",
                    ProviderBlockedStatus,
                    "previous last_once fire requires a new chat before continuing",
                    "open new chat")
                : IdeExplainability.New(
                    "ignite.continuity",
                    "awaiting_operator",
                    "last_once already fired and is latched awaiting operator",
                    "cdp_ignite op=resume")),
            hint = IsProviderBlockedStatus(latch.Status)
                ? "provider blocked — open new chat for PF; op=resume after handoff. force=true to replace."
                : "last_once already awaiting operator — do not repeat. force=true to replace, or disarm/resume to fly again."
        };
    }

    /// <summary>
    /// Continuity timer re-arm replaces prior work timers. tool-wake-* / remount-wake-* also use
    /// when=timer — keep those. Never supersede an arm already mid-CDT (status=firing).
    /// Caller must hold <see cref="Gate"/>.
    /// </summary>
    static void SupersedePriorContinuityTimersUnlocked(IgniteArm arm, List<string> cancelIds)
    {
        if (arm.Event != "timer" || IsSystemWakeArmId(arm.Id))
            return;

        foreach (var old in Arms.Where(a =>
                     a.Event == "timer"
                     && a.Status == "armed"
                     && !IsSystemWakeArmId(a.Id)
                     && !a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase))
                 .ToArray())
        {
            cancelIds.Add(old.Id);
            Arms.Remove(old);
        }
    }

    static object ArmSuccessPayload(IgniteArm arm) => new
    {
        schema = IdeIgniteChannel.Schema,
        ok = true,
        op = "arm",
        go = IdeIgniteChannel.GoName,
        tool = IdeIgniteChannel.ToolName,
        pulse = $"ignite · armed · {arm.Event}"
                + (arm.LastOnce ? " · last_once" : "")
                + (arm.DueUtc is { } d0 ? $" · due {d0:HH:mm:ss}Z" : ""),
        arm = Slim(arm),
        arms = SceneSlice(),
        explain = ExplainCardObject(IdeExplainability.New(
            $"ignite.{arm.Event}",
            arm.LastOnce ? "armed_last_once" : "armed",
            !string.IsNullOrWhiteSpace(arm.Task)
                ? $"authorized task '{arm.Task}' is active for continuity"
                : $"continuity arm is active for event '{arm.Event}'",
            arm.LastOnce ? "end turn" : "wait for event")),
        hint = arm.LastOnce
            ? "last_once: fires once → awaiting latch; harness blocks repeat idle re-arms until force/disarm/work arm."
            : arm.Event == "timer"
                ? "Harness fires when due — end your turn; no terminal poll loop."
                : $"Harness fires on {arm.Event} (ok_only={arm.OkOnly}). Kick cdp_build/cdp_test then end turn."
    };
}
