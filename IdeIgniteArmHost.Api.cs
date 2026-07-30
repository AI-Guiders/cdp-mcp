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
            return LatchEpicClosedAwait(arm, ProbeFlight());

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

    /// <summary>Explicit solo plateau: latch awaiting without CDT fire (epic closed / wait operator).</summary>
    public static object AwaitOperator(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        EnsureStarted();
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var task = Opt(args, "task") ?? Opt(args, "message") ?? Opt(args, "label")
                   ?? "epic closed — await operator";
        var id = Opt(args, "id");
        if (string.IsNullOrWhiteSpace(id))
            id = "arm-await-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                 + "-" + Guid.NewGuid().ToString("N")[..6];

        var arm = new IgniteArm
        {
            Id = id!,
            Event = "plateau",
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

        return LatchEpicClosedAwait(arm, ProbeFlight());
    }

    static object LatchEpicClosedAwait(IgniteArm arm, ContinuityFlight flight)
    {
        var reason = EpicClosedReason(flight);
        if (reason == "fly")
            reason = "await_operator";

        arm.LastOnce = true;
        arm.Once = true;
        arm.Status = "awaiting";
        arm.DueUtc = null;
        if (string.IsNullOrWhiteSpace(arm.Event) || arm.Event is "timer" or "manual")
            arm.Event = "plateau";

        lock (Gate)
        {
            // Latch awaiting — drop continuity work timers, keep event/system wakes + mid-CDT.
            Arms.RemoveAll(a => a.Status is "awaiting" or ProviderBlockedStatus);
            Arms.RemoveAll(a =>
                a.Status == "armed"
                && !IsEventTriggeredArm(a.Event)
                && !IsSystemWakeArmId(a.Id));
            Arms.RemoveAll(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
            Arms.Add(arm);
            PersistUnlocked();
        }

        PublishGlass();
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "await_operator",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            skipped = true,
            epic_closed = true,
            error = "epic_closed",
            reason,
            continuity = "awaiting_operator",
            pulse = $"ignite · epic closed · await operator · {reason}",
            arm = Slim(arm),
            arms = SceneSlice(),
            continuity_slice = ContinuitySlice(list),
            explain = ExplainCardObject(IdeExplainability.New(
                "ignite.continuity",
                "epic_closed",
                $"solo plateau ({reason}) — do not invent next epic; wait for operator",
                "cdp_ignite op=resume after operator pick")),
            hint = "Epic closed / await operator. Do not re-ARM last_once. op=resume when operator seeds next work; force=true only for explicit override."
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
