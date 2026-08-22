#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    internal enum WakeChargeTier
    {
        Minimal,
        Full,
    }

    /// <summary>Live TM + tier picked at fire time — agent must not re-probe empty TM.</summary>
    internal readonly record struct WakeChargePreflight(WakeChargeTier Tier, string TmStatusLine)
    {
        public static WakeChargePreflight Probe()
        {
            try
            {
                if (!IdeStageCycle.TryWorkspace(out var store, out var state, out _))
                    return Full("TM: unbound — workspace not loaded; cdp_open then go=plan; treat as amnesia until TM seeded.");

                var snap = store.TaskManagerSnapshot(state);
                if (snap.Features.Count == 0)
                    return Full("TM: empty — no features; seed from sealed course (go=plan feature … task …), not board hygiene.");

                var leafId = IdeIgniteArmHost.ResolveWakeLeafId(store, state);
                if (leafId is null)
                    return Full("TM: no incomplete leaf — all done/parked; seed next leaf from sealed course before invent.");

                var title = store.StageTitle(state, leafId.Value)?.Trim();
                if (string.IsNullOrWhiteSpace(title))
                    return Full("TM: incomplete leaf has no title — go=plan focus/done hygiene, then resume.");

                var feature = snap.ActiveFeatureTitle?.Trim();
                if (string.IsNullOrWhiteSpace(feature))
                    feature = "—";

                var focused = state.ActiveStageId == leafId;
                var mark = focused ? "[>]" : "[ ]";
                return new WakeChargePreflight(
                    WakeChargeTier.Minimal,
                    $"TM: {mark} {title} · feature={feature}");
            }
            catch
            {
                return Full("TM: probe error — assume amnesia; cdp_pressure op=recall then go=plan.");
            }
        }

        public static WakeChargePreflight ForHabitatLatch()
        {
            var live = Probe();
            return new WakeChargePreflight(WakeChargeTier.Full, live.TmStatusLine);
        }

        static WakeChargePreflight Full(string tmLine) =>
            new(WakeChargeTier.Full, tmLine);
    }

    internal static string FormatTmStatusForTests(string line) =>
        line.StartsWith("TM:", StringComparison.Ordinal) ? line : "TM: " + line;
}
