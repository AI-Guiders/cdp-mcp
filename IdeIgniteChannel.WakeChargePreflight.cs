#nullable enable

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
            var context = WakePreflightContext.Capture();
            return WakeChargeTierStrategyChain.Select(context);
        }

        public static WakeChargePreflight ForHabitatLatch()
        {
            var live = Probe();
            return new WakeChargePreflight(WakeChargeTier.Full, live.TmStatusLine);
        }

        internal static WakeChargePreflight Full(string tmLine) =>
            new(WakeChargeTier.Full, tmLine);
    }

    internal static string FormatTmStatusForTests(string line) =>
        line.StartsWith("TM:", StringComparison.Ordinal) ? line : "TM: " + line;
}
