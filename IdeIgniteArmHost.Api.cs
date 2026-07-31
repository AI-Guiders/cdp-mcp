#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    public static IReadOnlyList<IgniteArm> Snapshot()
    {
        EnsureLoaded();
        lock (Gate) return Arms.Select(Clone).ToList();
    }

    public static object SceneSlice()
    {
        var list = Snapshot().Where(a => a.Status is "armed" or "firing" or "error" or "awaiting" or ProviderBlockedStatus).ToList();
        return new
        {
            count = list.Count,
            armed = list.Select(Slim).ToList(),
            explain = ExplainCardObject(ContinuityExplain(list))
        };
    }

    public static object Arm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureStarted();
        if (!TryCreateArm(args, out var arm, out var err))
            return err!;

        var force = OptBool(args, "force") == true;
        if (arm.LastOnce && !force && IsEpicClosed(ProbeFlight()))
            return LatchPartnerAwait(arm, ProbeFlight(), halt: false);

        var cancelIds = new List<string>();
        lock (Gate)
        {
            if (TrySkipReArmWhenLatched(arm, force) is { } skipped)
                return skipped;

            if (!arm.LastOnce || force)
            {
                Arms.RemoveAll(a => a.Status == "awaiting");
                Arms.RemoveAll(a => a.Status == ProviderBlockedStatus);
            }

            SupersedePriorContinuityTimersUnlocked(arm, cancelIds);

            Arms.RemoveAll(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
            Arms.Add(arm);
            PersistUnlocked();
        }

        foreach (var cancelId in cancelIds)
            CancelInFlightFire(cancelId);

        return ArmSuccessPayload(arm);
    }

    /// <summary>Legacy alias — prefer <see cref="AwaitPartner"/>.</summary>
    public static object AwaitOperator(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        AwaitPartner(args, halt: false);

    /// <summary>Explicit epic close: latch awaiting partner without CDT fire. Soft invent-ban (keeps system wakes).</summary>
    public static object AwaitPartner(IReadOnlyDictionary<string, JsonElement>? args = null, bool halt = false)
    {
        EnsureStarted();
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var task = Opt(args, "task") ?? Opt(args, "message") ?? Opt(args, "label")
                   ?? (halt ? "halt — await partner" : "epic closed — await partner");
        var id = Opt(args, "id");
        if (string.IsNullOrWhiteSpace(id))
            id = (halt ? "arm-halt-" : "arm-await-")
                 + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                 + "-" + Guid.NewGuid().ToString("N")[..6];

        var arm = new IgniteArm
        {
            Id = id!,
            Event = halt ? "halt" : "plateau",
            Message = IdeIgniteChannel.CanonicalComposerCharge,
            ChargeMode = "minimal",
            Task = task,
            Port = OptInt(args, "port") ?? IdeIgniteChannel.DefaultPort,
            Once = true,
            LastOnce = true,
            OkOnly = true,
            SettleSeconds = 0,
            WaitSeconds = 90,
            DueUtc = null,
            Status = "awaiting",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        return LatchPartnerAwait(arm, ProbeFlight(), halt);
    }

    /// <summary>
    /// Conscious stop-world until partner: autonomous off + HILD off + clear all arms (no reseed) + await-partner latch.
    /// Distinct from <c>disarm all</c> (keeps autonomy means) and soft <c>await_partner</c> (epic invent-ban).
    /// </summary>
    public static object Halt(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var why = Opt(args, "why") ?? "op=halt";
        SetAutonomous(false, why);
        SetHild(false, why);

        List<string> cancelled;
        lock (Gate)
        {
            EnsureLoaded();
            cancelled = Arms.Select(a => a.Id).ToList();
            Arms.Clear();
            PersistUnlocked();
        }

        foreach (var doomedId in cancelled)
            CancelInFlightFire(doomedId);
        CancelAllInFlightFires();

        return AwaitPartner(args, halt: true);
    }

    static object LatchPartnerAwait(IgniteArm arm, ContinuityFlight flight, bool halt)
    {
        var reason = EpicClosedReason(flight);
        if (reason == "fly")
            reason = halt ? "halt" : "await_partner";

        arm.LastOnce = true;
        arm.Once = true;
        arm.Status = "awaiting";
        arm.DueUtc = null;
        if (halt)
            arm.Event = "halt";
        else if (string.IsNullOrWhiteSpace(arm.Event) || arm.Event is "timer" or "manual")
            arm.Event = "plateau";

        lock (Gate)
        {
            Arms.RemoveAll(a => a.Status is "awaiting" or ProviderBlockedStatus);
            if (halt)
            {
                // Stop-world: drop everything except this latch (in-flight already cancelled by Halt).
                Arms.RemoveAll(a => !a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Soft epic close — drop continuity work timers, keep event/system wakes + mid-CDT.
                Arms.RemoveAll(a =>
                    a.Status == "armed"
                    && !IsEventTriggeredArm(a.Event)
                    && !IsSystemWakeArmId(a.Id));
            }

            Arms.RemoveAll(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
            Arms.Add(arm);
            PersistUnlocked();
        }

        PublishGlass();
        var list = Snapshot();
        var opName = halt ? "halt" : "await_partner";
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = opName,
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            skipped = true,
            halted = halt,
            epic_closed = !halt,
            error = halt ? "halted" : "epic_closed",
            reason,
            continuity = "awaiting_partner",
            await_partner = true,
            await_operator = true, // legacy alias
            pulse = halt
                ? $"ignite · halt · await partner · {reason}"
                : $"ignite · epic closed · await partner · {reason}",
            arm = Slim(arm),
            arms = SceneSlice(),
            continuity_slice = ContinuitySlice(list),
            autonomous = IsAutonomousArmed(),
            hild = IsHildArmed(),
            explain = ExplainCardObject(IdeExplainability.New(
                "ignite.continuity",
                halt ? "halted" : "epic_closed",
                halt
                    ? $"stop-world ({reason}) — autonomous/HILD off; wait for partner"
                    : $"solo plateau ({reason}) — do not invent next epic; wait for partner",
                "cdp_ignite op=resume after partner pick")),
            hint = halt
                ? "Halt: world stopped until partner. op=resume then re-ARM autonomous/HILD/last_once as needed."
                : "Epic closed / await partner. Do not re-ARM last_once. op=resume when partner seeds next work; force=true only for explicit override."
        };
    }

    static object SceneSliceUnlocked()
    {
        var list = Arms.Where(a => a.Status is "armed" or "firing" or "error" or "awaiting" or ProviderBlockedStatus).Select(Clone).ToList();
        return new
        {
            count = list.Count,
            armed = list.Select(Slim).ToList(),
            explain = ExplainCardObject(ContinuityExplain(list))
        };
    }

}
