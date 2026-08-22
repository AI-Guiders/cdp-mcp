#nullable enable

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    internal interface IWakeChargeTierStrategy
    {
        bool Applies(WakePreflightContext context);
        WakeChargePreflight Select(WakePreflightContext context);
    }

    internal static class WakeChargeTierStrategyChain
    {
        static readonly IWakeChargeTierStrategy[] Ordered =
        [
            new WakeProbeFaultStrategy(),
            new WakeUnboundWorkspaceStrategy(),
            new WakeEmptyFeaturesStrategy(),
            new WakeNoIncompleteLeafStrategy(),
            new WakeMissingLeafTitleStrategy(),
            new WakeFocusedLeafStrategy(),
        ];

        public static WakeChargePreflight Select(WakePreflightContext context)
        {
            foreach (var strategy in Ordered)
            {
                if (!strategy.Applies(context))
                    continue;
                return WakeChargePressureAutoFullPolicy.Apply(context, strategy.Select(context));
            }

            throw new InvalidOperationException("wake tier strategy chain fell through");
        }
    }

    /// <summary>Minimal + empty hot stash → Full (compaction insurance).</summary>
    internal static class WakeChargePressureAutoFullPolicy
    {
        public static WakeChargePreflight Apply(WakePreflightContext context, WakeChargePreflight selected)
        {
            if (selected.Tier != WakeChargeTier.Minimal || context.HotStashPresent)
                return selected;

            return WakeChargePreflight.Full(
                selected.TmStatusLine + " · pressure=empty (hot stash missing — full wake)");
        }
    }

    sealed class WakeProbeFaultStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) => context.Faulted;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: probe error — assume amnesia; cdp_pressure op=recall then go=plan.");
    }

    sealed class WakeUnboundWorkspaceStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) => !context.Faulted && !context.WorkspaceBound;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: unbound — workspace not loaded; cdp_open then go=plan; treat as amnesia until TM seeded.");
    }

    sealed class WakeEmptyFeaturesStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) =>
            context.WorkspaceBound && context.FeatureCount == 0;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: empty — no features; seed from sealed course (go=plan feature … task …), not board hygiene.");
    }

    sealed class WakeNoIncompleteLeafStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) =>
            context.WorkspaceBound && context.FeatureCount > 0 && context.WakeLeafId is null;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: no incomplete leaf — all done/parked; seed next leaf from sealed course before invent.");
    }

    sealed class WakeMissingLeafTitleStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) =>
            context.WakeLeafId is not null && string.IsNullOrWhiteSpace(context.LeafTitle);

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: incomplete leaf has no title — go=plan focus/done hygiene, then resume.");
    }

    sealed class WakeFocusedLeafStrategy : IWakeChargeTierStrategy
    {
        public bool Applies(WakePreflightContext context) =>
            context.WakeLeafId is not null && !string.IsNullOrWhiteSpace(context.LeafTitle);

        public WakeChargePreflight Select(WakePreflightContext context)
        {
            var mark = context.LeafFocused ? "[>]" : "[ ]";
            return new WakeChargePreflight(
                WakeChargeTier.Minimal,
                $"TM: {mark} {context.LeafTitle} · feature={context.FeatureTitle}");
        }
    }
}
