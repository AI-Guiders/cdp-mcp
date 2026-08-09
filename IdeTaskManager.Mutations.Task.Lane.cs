#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeTaskManager
{
    /// <summary>Switch Who focus lane on shared crew board (Multi-principal).</summary>
    static object TaskSetLane(
        IntentWorkspaceStore store,
        IntentWorkspaceState state,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = Opt(args, "lane")
                  ?? OptGoArg(args, "lane")
                  ?? Opt(args, "executor")
                  ?? OptGoArg(args, "executor")
                  ?? Title(args);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("lane needs Who — lane Кир | lane Sierra | lane clear");

        var who = IntentWorkspaceStore.NormalizeExecutor(raw);
        var target = who
                     ?? (IsLaneClear(raw)
                         ? IntentWorkspaceStore.DefaultFocusLane
                         : throw new ArgumentException("lane needs Who — lane Кир | lane Sierra | lane clear"));

        var before = state.FocusLane;
        store.WorkFocusSwitchLane(state, target);
        return new
        {
            op = "lane",
            lane = state.FocusLane,
            previous_lane = before,
            task_id = state.ActiveStageId,
            feature_id = state.ActiveIntentId,
            hint = "Shared board · per-Who focus. Other lanes keep their [»] leaf."
        };
    }

    static bool IsLaneClear(string raw)
    {
        var t = raw.Trim().TrimStart('~', '@');
        return t.Equals("clear", StringComparison.OrdinalIgnoreCase)
               || t.Equals("none", StringComparison.OrdinalIgnoreCase)
               || t.Equals("off", StringComparison.OrdinalIgnoreCase)
               || t.Equals("-", StringComparison.Ordinal)
               || t.Equals(IntentWorkspaceStore.DefaultFocusLane, StringComparison.OrdinalIgnoreCase);
    }
}
