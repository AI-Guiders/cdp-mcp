#nullable enable
using CdpMcp.Habitat;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    internal static class WakeChargeTierStrategyChain
    {
        static readonly IRule<WakePreflightContext, WakeChargePreflight>[] Ordered =
        [
            new WakeProbeFaultRule(),
            new WakeUnboundWorkspaceRule(),
            new WakeEmptyFeaturesRule(),
            new WakeNoIncompleteLeafRule(),
            new WakeMissingLeafTitleRule(),
            new WakeFocusedLeafRule(),
        ];

        public static WakeChargePreflight Select(WakePreflightContext context) =>
            RuleChain.Pipe(
                RuleChain.FirstMatch(context, Ordered),
                p => WakeChargePressureAutoFullPolicy.Apply(context, p));
    }

    /// <summary>Minimal + no recall SSOT (tenant/peer/wake latch) → Full (compaction insurance).</summary>
    internal static class WakeChargePressureAutoFullPolicy
    {
        public static WakeChargePreflight Apply(WakePreflightContext context, WakeChargePreflight selected)
        {
            if (selected.Tier != WakeChargeTier.Minimal || context.HotStashPresent)
                return selected;

            return WakeChargePreflight.Full(
                selected.TmStatusLine + " · pressure=empty (recall SSOT missing — full wake)");
        }
    }

    sealed class WakeProbeFaultRule : IRule<WakePreflightContext, WakeChargePreflight>
    {
        public bool Applies(WakePreflightContext context) => context.Faulted;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: probe error — assume amnesia; cdp_pressure op=recall then go=plan.");
    }

    sealed class WakeUnboundWorkspaceRule : IRule<WakePreflightContext, WakeChargePreflight>
    {
        public bool Applies(WakePreflightContext context) => !context.Faulted && !context.WorkspaceBound;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: unbound — workspace not loaded; cdp_open then go=plan; treat as amnesia until TM seeded.");
    }

    sealed class WakeEmptyFeaturesRule : IRule<WakePreflightContext, WakeChargePreflight>
    {
        public bool Applies(WakePreflightContext context) =>
            context.WorkspaceBound && context.FeatureCount == 0;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: empty — no features; seed from sealed course (go=plan feature … task …), not board hygiene.");
    }

    sealed class WakeNoIncompleteLeafRule : IRule<WakePreflightContext, WakeChargePreflight>
    {
        public bool Applies(WakePreflightContext context) =>
            context.WorkspaceBound && context.FeatureCount > 0 && context.WakeLeafId is null;

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: no incomplete leaf — all done/parked; seed next leaf from sealed course before invent.");
    }

    sealed class WakeMissingLeafTitleRule : IRule<WakePreflightContext, WakeChargePreflight>
    {
        public bool Applies(WakePreflightContext context) =>
            context.WakeLeafId is not null && string.IsNullOrWhiteSpace(context.LeafTitle);

        public WakeChargePreflight Select(WakePreflightContext context) =>
            WakeChargePreflight.Full("TM: incomplete leaf has no title — go=plan focus/done hygiene, then resume.");
    }

    sealed class WakeFocusedLeafRule : IRule<WakePreflightContext, WakeChargePreflight>
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
