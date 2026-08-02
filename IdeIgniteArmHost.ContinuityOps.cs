#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
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

}

