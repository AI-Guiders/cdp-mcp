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
}

