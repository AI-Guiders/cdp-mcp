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
        var list = Snapshot().Where(a => a.Status is "armed" or "firing" or "error" or "awaiting").ToList();
        return new
        {
            count = list.Count,
            armed = list.Select(Slim).ToList()
        };
    }

    public static object Arm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureStarted();
        if (!TryCreateArm(args, out var arm, out var err))
            return err!;

        var force = OptBool(args, "force") == true;
        lock (Gate)
        {
            var awaiting = Arms.Where(a => a.Status == "awaiting").ToList();
            if (arm.LastOnce && awaiting.Count > 0 && !force)
            {
                var latch = awaiting[0];
                return new
                {
                    schema = IdeIgniteChannel.Schema,
                    ok = true,
                    op = "arm",
                    skipped = true,
                    error = "awaiting_operator",
                    pulse = $"ignite · awaiting · skip re-arm · {latch.Id}",
                    arm = Slim(latch),
                    arms = SceneSliceUnlocked(),
                    hint = "last_once already awaiting operator — do not repeat. force=true to replace, or disarm/resume to fly again."
                };
            }

            if (!arm.LastOnce || force)
                Arms.RemoveAll(a => a.Status == "awaiting");

            Arms.RemoveAll(a => a.Id.Equals(arm.Id, StringComparison.OrdinalIgnoreCase));
            Arms.Add(arm);
            PersistUnlocked();
        }

        return new
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
            hint = arm.LastOnce
                ? "last_once: fires once → awaiting latch; harness blocks repeat idle re-arms until force/disarm/work arm."
                : arm.Event == "timer"
                    ? "Harness fires when due — end your turn; no shell watcher."
                    : $"Harness fires on {arm.Event} (ok_only={arm.OkOnly}). Kick cdp_build/cdp_test then end turn."
        };
    }

    static object SceneSliceUnlocked()
    {
        var list = Arms.Where(a => a.Status is "armed" or "firing" or "error" or "awaiting").Select(Clone).ToList();
        return new
        {
            count = list.Count,
            armed = list.Select(Slim).ToList()
        };
    }

    public static object Disarm(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        var id = Opt(args, "id") ?? Opt(args, "arm");
        var all = OptBool(args, "all") == true
                  || string.Equals(Opt(args, "when") ?? Opt(args, "event"), "all", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(id, "all", StringComparison.OrdinalIgnoreCase);

        int removed;
        lock (Gate)
        {
            if (all)
            {
                removed = Arms.Count;
                Arms.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(id))
            {
                removed = Arms.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                return Err("disarm", "id_required", "disarm id=… or all=true");
            }

            PersistUnlocked();
        }

        return new
        {
            schema = IdeIgniteChannel.Schema,
            ok = true,
            op = "disarm",
            pulse = $"ignite · disarmed · {removed}",
            removed,
            arms = SceneSlice()
        };
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
            ops_pulse = IdeOpsPulse.Line(),
            seat = Seat,
            arms = list.Select(Slim).ToList(),
            store = StorePath,
            hint = "op=arm when=build_finished|test_finished|timer in=5m message=… [task=…]; op=hygiene|plateau to clear noise"
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
            hint = "Plateau clean. Continuity arms intact; re-ARM short timer before end turn."
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
            arms = SceneSlice()
        };
    }

    /// <summary>Clear awaiting latch so flight can continue (or disarm awaiting arms).</summary>
    public static object Resume(IReadOnlyDictionary<string, JsonElement> args)
    {
        EnsureLoaded();
        int removed;
        lock (Gate)
        {
            removed = Arms.RemoveAll(a => a.Status == "awaiting");
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
            pulse = $"ignite · resume · cleared awaiting={removed}",
            removed,
            continuity = ContinuitySlice(list),
            arms = SceneSlice(),
            hint = removed > 0
                ? "Await latch cleared — re-ARM work timer if needed."
                : "No awaiting latch. op=arm last_once=true for Await Operator mode."
        };
    }

    internal static object ContinuitySlice(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var armed = list.Where(a => a.Status is "armed" or "firing").ToList();
        var awaiting = list.Where(a => a.Status == "awaiting").ToList();
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
            error = errors,
            await_operator = awaiting.Count > 0,
            await_arm = awaiting.Select(Slim).FirstOrDefault(),
            next_due = next,
            tasks = armed.Select(a => a.Task).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
        };
    }

    internal static string ContinuityPulseLine(IReadOnlyList<IgniteArm>? list = null)
    {
        list ??= Snapshot();
        var awaiting = list.Count(a => a.Status == "awaiting");
        if (awaiting > 0)
            return $"ignite · continuity · awaiting_operator · latch={awaiting}";
        var armed = list.Count(a => a.Status is "armed" or "firing");
        var err = list.Count(a => a.Status == "error");
        var next = list
            .Where(a => (a.Status is "armed" or "firing") && a.DueUtc is not null)
            .OrderBy(a => a.DueUtc)
            .FirstOrDefault();
        var due = next?.DueUtc is { } d ? $" · next {d:HH:mm:ss}Z" : "";
        var noise = err > 0 ? $" · stale={err}" : "";
        return $"ignite · continuity · armed={armed}{noise}{due}";
    }

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
                Firing.TryRemove(id, out _);
            if (persist) PersistUnlocked();
        }

        return removed;
    }

}
