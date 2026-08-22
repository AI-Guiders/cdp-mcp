#nullable enable
using CdpMcp.Habitat;

namespace CdpMcp;

internal static partial class IdeIgniteArmHost
{
    internal readonly record struct FireChargeComposeContext(
        IgniteArm Arm,
        bool Ok,
        string? Pulse,
        string? Detail,
        string? ProjectRoot,
        string? FocusHint);

    static string ComposeFireCharge(IgniteArm arm, bool ok, string? pulse, string? detail) =>
        FireChargeComposeRuleChain.Select(new FireChargeComposeContext(
            arm,
            ok,
            pulse,
            detail,
            IdePressureChannel.TryPeekProjectRoot(),
            IdeDomainPulse.FocusHintFromPlanLatch()));

    static class FireChargeComposeRuleChain
    {
        static readonly IRule<FireChargeComposeContext, string>[] Ordered =
        [
            new CustomFireChargeRule(),
            new RemountFireChargeRule(),
            new OomFireChargeRule(),
            new EscalateFireChargeRule(),
            new MinimalFireChargeRule(),
        ];

        public static string Select(FireChargeComposeContext context) =>
            RuleChain.FirstMatch(context, Ordered);
    }

    sealed class CustomFireChargeRule : IRule<FireChargeComposeContext, string>
    {
        public bool Applies(FireChargeComposeContext context) =>
            IsCustomChargeMode(context.Arm.ChargeMode);

        public string Select(FireChargeComposeContext context) =>
            IdeIgniteChannel.SanitizeComposerCharge(
                Expand(context.Arm.Message, context.Arm, context.Ok, context.Pulse, context.Detail));
    }

    sealed class RemountFireChargeRule : IRule<FireChargeComposeContext, string>
    {
        public bool Applies(FireChargeComposeContext context) =>
            IsRemountChargeMode(context.Arm.ChargeMode);

        public string Select(FireChargeComposeContext context) =>
            IdeIgniteChannel.ComposeRemountInitializedCharge(context.ProjectRoot, context.FocusHint);
    }

    sealed class OomFireChargeRule : IRule<FireChargeComposeContext, string>
    {
        public bool Applies(FireChargeComposeContext context) =>
            IsOomChargeMode(context.Arm.ChargeMode);

        public string Select(FireChargeComposeContext context) =>
            IdeIgniteChannel.ComposeOomWakeCharge(context.ProjectRoot, context.FocusHint);
    }

    sealed class EscalateFireChargeRule : IRule<FireChargeComposeContext, string>
    {
        public bool Applies(FireChargeComposeContext context) =>
            IsEscalateChargeMode(context.Arm.ChargeMode);

        public string Select(FireChargeComposeContext context) =>
            IdeIgniteChannel.ComposeEscalateWakeCharge(context.ProjectRoot, context.FocusHint);
    }

    sealed class MinimalFireChargeRule : IRule<FireChargeComposeContext, string>
    {
        public bool Applies(FireChargeComposeContext context) => true;

        public string Select(FireChargeComposeContext context)
        {
            var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
            var tier = IsMinimalChargeMode(context.Arm.ChargeMode)
                ? IdeIgniteChannel.WakeChargeTier.Minimal
                : preflight.Tier;
            return IdeIgniteChannel.ComposeWakeBody(preflight, tier);
        }
    }
}
