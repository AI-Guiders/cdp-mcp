#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    public static object Disarm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        var id = Opt(args, "id") ?? Opt(args, "arm");
        var all = OptBool(args, "all") == true
                  || string.Equals(Opt(args, "when") ?? Opt(args, "event"), "all", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(id, "all", StringComparison.OrdinalIgnoreCase);
        // Under autonomous, all=true must not suicide continuity means unless force=true.
        var force = OptBool(args, "force") == true;

        int removed;
        var kept = new List<string>();
        var cancelledIds = new List<string>();
        string? cancelId = null;
        var cancelAll = false;
        var exceptAutonomy = false;
        lock (Gate)
        {
            if (all)
            {
                if (IsAutonomousArmed() && !force)
                {
                    exceptAutonomy = true;
                    var doomed = Arms.Where(a => !IsAutonomyMeansArm(a)).Select(a => a.Id).ToList();
                    kept = Arms.Where(IsAutonomyMeansArm).Select(a => a.Id).ToList();
                    removed = Arms.RemoveAll(a => doomed.Contains(a.Id, StringComparer.OrdinalIgnoreCase));
                    cancelledIds.AddRange(doomed);
                }
                else
                {
                    removed = Arms.Count;
                    Arms.Clear();
                    cancelAll = true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(id))
            {
                removed = Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                cancelId = id;
            }
            else
            {
                return Err("disarm", "id_required", "disarm id=… or all=true");
            }

            PersistUnlocked();
        }

        // Always cancel in-flight CDT — store remove alone left FireAsync(CancellationToken.None)
        // waiting for composer idle, then injecting a stale "still running" charge (tool-wake noise).
        if (cancelAll)
            CancelAllInFlightFires();
        else if (!string.IsNullOrWhiteSpace(cancelId))
            CancelInFlightFire(cancelId);
        else
        {
            foreach (var doomedId in cancelledIds)
                CancelInFlightFire(doomedId);
        }

        object? seed = null;
        // Autonomous latch on + no live wake path → plant seed (cannot suicide while autonomous).
        if (IsAutonomousArmed() && !HasLiveWakePathUnlocked())
            seed = AutonomousContinue(exceptAutonomy || force
                ? "disarm_all_under_autonomous"
                : "disarm_emptied_wake_path");

        var pulse = exceptAutonomy
            ? $"ignite · disarmed · {removed} · kept autonomy means {kept.Count}"
            : $"ignite · disarmed · {removed}";

        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "disarm",
            pulse,
            removed,
            except_autonomy = exceptAutonomy,
            kept,
            force,
            seed,
            arms = SceneSlice(),
            continuity = ContinuitySlice(),
            hint = exceptAutonomy
                ? "autonomous: all=true cleared work arms only; kept seed/leaf/hild/remount/tool wakes. force=true to clear those too (still re-seeds while autonomous latch on)."
                : force && IsAutonomousArmed()
                    ? "force cleared store; autonomous latch still on — seed wake planted if wake path was empty."
                    : null
        };
    }

    /// <summary>Infrastructure arms that keep the agent able to wake under autonomous latch.</summary>
    internal static bool IsAutonomyMeansArm(IgniteArm a)
    {
        if (a.Id.Equals(AutonomousSeedArmId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Id.Equals(LeafWakeArmId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Id.StartsWith(HildArmIdPrefix, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Id.StartsWith(HildEscalateArmIdPrefix, StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Id.StartsWith("remount-wake-", StringComparison.OrdinalIgnoreCase))
            return true;
        if (a.Id.StartsWith("tool-wake-", StringComparison.OrdinalIgnoreCase))
            return true;
        // Mid-flight event wakes — do not cancel build/test/shell inject under all=.
        if (a.Status == "firing"
            && a.Event is "build_finished" or "test_finished" or "shell_finished" or "human_away")
            return true;
        return false;
    }

    static bool HasLiveWakePathUnlocked()
    {
        lock (Gate)
        {
            return Arms.Any(a => a.Status is "armed" or "firing");
        }
    }

    public static object List()
    {
        EnsureLoaded();
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "list",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = ContinuityPulseLine(list),
            continuity = ContinuitySlice(list),
            explain = ExplainCardObject(ContinuityExplain(list)),
            ops_pulse = IdeOpsPulse.Line(),
            seat = Seat,
            arms = list.Select(Slim).ToList(),
            store = StorePath,
            hint = "op=arm when=build_finished|test_finished|timer in=5m task=… [last_once]; charge=minimal (default) fires canonical wake + amnesia postfix; op=hygiene|plateau"
        };
    }

    /// <summary>Clear operational noise (error / once mid-fire zombies). Keeps armed continuity.</summary>
    public static object Hygiene()
    {
        EnsureLoaded();
        var removed = SweepNoiseUnlocked(persist: true);
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "hygiene",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · hygiene · removed {removed.Count} · {ContinuityPulseLine(list)}",
            removed,
            continuity = ContinuitySlice(list),
            arms = SceneSlice(),
            explain = ExplainCardObject(ContinuityExplain(list)),
            hint = "Kept armed continuity. Re-ARM timer if store empty."
        };
    }

    /// <summary>Plateau gesture: same as hygiene — keep continuity, drop stale noise.</summary>
    public static object Plateau()
    {
        EnsureLoaded();
        var removed = SweepNoiseUnlocked(persist: true);
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "plateau",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · plateau · kept armed · scrubbed {removed.Count}",
            removed,
            continuity = ContinuitySlice(list),
            arms = SceneSlice(),
            explain = ExplainCardObject(ContinuityExplain(list)),
            hint = "Plateau clean. Continuity arms intact. On epic @handoff use op=await_partner (or halt for stop-world)."
        };
    }

    public static object Continuity()
    {
        EnsureLoaded();
        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "continuity",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = ContinuityPulseLine(list),
            continuity = ContinuitySlice(list),
            arms = SceneSlice(),
            explain = ExplainCardObject(ContinuityExplain(list))
        };
    }

    /// <summary>Clear awaiting latch so flight can continue (or disarm awaiting arms).</summary>
    public static object Resume(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        int removed;
        lock (Gate)
        {
            removed = Arms.RemoveAll(a => a.Status is "awaiting" or ProviderBlockedStatus);
            if (removed > 0) PersistUnlocked();
        }

        var list = Snapshot();
        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "resume",
            go = IdeIgniteChannel.GoName,
            tool = IdeIgniteChannel.ToolName,
            pulse = $"ignite · resume · cleared latch={removed}",
            removed,
            continuity = ContinuitySlice(list),
            arms = SceneSlice(),
            explain = ExplainCardObject(removed > 0
                ? IdeExplainability.New(
                    "ignite.resume",
                    "latch_cleared",
                    "awaiting/provider_blocked latch was cleared so continuity can move again",
                    "re-arm if needed")
                : ContinuityExplain(list)),
            hint = removed > 0
                ? "Latch cleared (awaiting and/or provider_blocked) — re-ARM on NEW chat title after PF handoff."
                : "No latch. op=arm last_once=true for Await Partner mode."
        };
    }

    internal static object ContinuitySlice(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var armed = list.Where(a => a.Status is "armed" or "firing").ToList();
        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
        var providerBlocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        var errors = list.Count(a => a.Status == "error");
        var next = armed
            .Where(a => a.DueUtc is not null)
            .OrderBy(a => a.DueUtc)
            .Select(a => new { a.Id, a.Task, a.DueUtc, a.Status, a.Event })
            .FirstOrDefault();
        return new
        {
            armed = armed.Count,
            firing = list.Count(a => a.Status == "firing"),
            awaiting = awaiting.Count,
            provider_blocked = providerBlocked.Count > 0,
            new_thread_required = providerBlocked.Count > 0,
            error = errors,
            await_partner = awaiting.Count > 0,
            await_operator = awaiting.Count > 0, // legacy alias
            autonomous = IsAutonomousArmed(),
            await_arm = awaiting.Select(Slim).FirstOrDefault(),
            blocked_arm = providerBlocked.Select(Slim).FirstOrDefault(),
            next_due = next,
            tasks = armed.Select(a => a.Task).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
        };
    }

    internal static string ContinuityPulseLine(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var blocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        if (blocked.Count > 0)
            return $"ignite · continuity · {ProviderBlockedStatus} · new_thread_required · latch={blocked.Count}";
        var awaiting = list.Count(a => a.Status == "awaiting");
        if (awaiting > 0)
            return $"ignite · continuity · awaiting_partner · latch={awaiting}";
        var auto = IsAutonomousArmed() ? " · autonomous" : "";
        var armed = list.Count(a => a.Status is "armed" or "firing");
        var err = list.Count(a => a.Status == "error");
        var next = list
            .Where(a => (a.Status is "armed" or "firing") && a.DueUtc is not null)
            .OrderBy(a => a.DueUtc)
            .FirstOrDefault();
        var due = next?.DueUtc is { } d ? $" · next {d:HH:mm:ss}Z" : "";
        var noise = err > 0 ? $" · stale={err}" : "";
        return $"ignite · continuity · armed={armed}{auto}{noise}{due}";
    }

    /// <summary>Mirror AutoIgnition continuity to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        var list = Snapshot();
        var armedCount = list.Count(a => a.Status is "armed" or "firing");
        var awaitingCount = list.Count(a => a.Status == "awaiting");
        var providerBlocked = list.Any(a => a.Status == ProviderBlockedStatus);
        var active = armedCount > 0 || awaitingCount > 0 || providerBlocked;
        CideIgniteLatch.Publish(active, ContinuityPulseLine(list), armedCount, awaitingCount, providerBlocked);
    }

    static IdeExplainability.ExplainCard ContinuityExplain(IReadOnlyList<IgniteArm> list)
    {
        var blocked = list.Where(a => a.Status == ProviderBlockedStatus).ToList();
        if (blocked.Count > 0)
            return IdeExplainability.New(
                "ignite.continuity",
                ProviderBlockedStatus,
                $"continuity latch count={blocked.Count} requires a new chat",
                "open new chat");

        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
        if (awaiting.Count > 0)
            return IdeExplainability.New(
                "ignite.continuity",
                "awaiting_partner",
                $"last_once latch count={awaiting.Count} is waiting for partner acknowledgement",
                "cdp_ignite op=resume");

        var armed = list.Where(a => a.Status is "armed" or "firing").OrderBy(a => a.DueUtc ?? DateTimeOffset.MaxValue).ToList();
        if (armed.Count > 0)
        {
            var next = armed[0];
            var reason = next.Event == "timer" ? "timer_wait" : $"{next.Event}_wait";
            var authority = next.DueUtc is { } due
                ? $"continuity is armed and next due is {due:HH:mm:ss}Z"
                : "continuity is armed and waiting for its event";
            return IdeExplainability.New("ignite.continuity", reason, authority, "wait for event");
        }

        return IdeExplainability.New(
            "ignite.continuity",
            "idle",
            "no armed or latched continuity remains",
            "arm if continuity is needed");
    }

    static object? ExplainCardObject(IdeExplainability.ExplainCard? explain) =>
        explain is null
            ? null
            : new
            {
                source = explain.Source,
                reason = explain.Reason,
                authority = explain.Authority,
                next_step = explain.NextStep,
                why = explain.WhyLine
            };

    /// <summary>Drop error arms + once stuck-firing with FiredUtc. Returns removed ids.</summary>
    static List<string> SweepNoiseUnlocked(bool persist)
    {
        List<string> removed;
        lock (Gate)
        {
            removed = Arms
                .Where(a => a.Status == "error"
                            || (a.Once && a.Status == "firing" && a.FiredUtc is not null))
                .Select(a => a.Id)
                .ToList();
            if (removed.Count == 0) return removed;
            var set = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
            Arms.RemoveAll(a => set.Contains(a.Id));
            foreach (var id in removed)
                CancelInFlightFire(id);
            if (persist) PersistUnlocked();
        }

        return removed;
    }
}
