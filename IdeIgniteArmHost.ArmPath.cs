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
            : "awaiting_partner";
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "arm",
            skipped = true,
            error = latchErr,
            continuity = IsProviderBlockedStatus(latch.Status) ? ProviderBlockedStatus : "awaiting_partner",
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
                    "awaiting_partner",
                    "last_once already fired and is latched awaiting partner",
                    "cdp_ignite op=resume")),
            hint = IsProviderBlockedStatus(latch.Status)
                ? "provider blocked — open new chat for PF; op=resume after handoff. force=true to replace."
                : "last_once already awaiting partner — do not repeat. force=true to replace, or disarm/resume to fly again."
        };
    }

    /// <summary>
    /// Continuity timer re-arm replaces prior <em>work</em> timers only.
    /// Keep: remount/tool wakes, mid-CDT (firing), and event wakes (build/test/shell).
    /// Caller must hold <see cref="Gate"/>.
    /// </summary>
    static void SupersedePriorContinuityTimersUnlocked(IgniteArm arm, List<string> cancelIds)
    {
        if (arm.Event != "timer" || IsSystemWakeArmId(arm.Id) || IsEventTriggeredArm(arm.Event))
            return;

        foreach (var old in Arms.Where(a =>
                     IsSupersedableContinuityWorkTimer(a)
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
            arm.LastOnce
                ? (IsAutonomousArmed()
                    ? LastOnceArmNextStep(autonomous: true)
                    : LastOnceArmNextStep(autonomous: false))
                : ContinuityArmedNextStep(IsAutonomousArmed()))),
        hint = arm.LastOnce
            ? LastOnceArmHint(IsAutonomousArmed())
              + (arm.InRaw is { } ir && ir.Contains("clamped", StringComparison.OrdinalIgnoreCase)
                  ? " Duration clamped to 3m under autonomous — 45m looks like sleep; force=true to override."
                  : "")
            : arm.Event == "timer"
                ? (IsAutonomousArmed()
                    ? "Harness fires when due — keep flying started TM leaf; timer is insurance, not a nap."
                    : "Harness fires when due — end your turn; no terminal poll loop.")
                : (IsAutonomousArmed()
                    ? $"Harness fires on {arm.Event} (ok_only={arm.OkOnly}). Kick cdp_build/cdp_test — keep flying; wake is insurance."
                    : $"Harness fires on {arm.Event} (ok_only={arm.OkOnly}). Kick cdp_build/cdp_test then end turn."),
    };

    /// <summary>Explain next_step after last_once arm — autonomous must not teach park-on-timer.</summary>
    internal static string LastOnceArmNextStep(bool autonomous) =>
        autonomous
            ? "keep flying started TM leaf; last_once is insurance only — do not park on the timer"
            : "end turn";

    /// <summary>Arm hint after last_once — ACC: insurance ≠ idle license while leaf started.</summary>
    internal static string LastOnceArmHint(bool autonomous) =>
        autonomous
            ? "last_once under autonomous: insurance if thread dies — NOT permission to idle while a TM leaf is started. Keep act; re-ARM after work."
            : "last_once: fires once → awaiting latch; harness blocks repeat idle re-arms until force/disarm/work arm.";
}
