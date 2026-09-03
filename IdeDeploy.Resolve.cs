#nullable enable

namespace CdpMcp;

internal static partial class IdeDeploy
{
    internal readonly record struct TargetDecision(
        bool Ok,
        string? Target,
        string? Sibling,
        string? TargetRaw,
        string? Error,
        string? Hint);

    internal static TargetDecision ResolveTarget(
        string? selfRoot,
        string seat,
        string? targetRaw,
        string mode,
        bool force)
    {
        var layout = Cdp.Deploy.CdpDeployLayout.Default;
        var sibling = layout.SiblingBridgeForSeat(seat);

        if (mode == "apply")
        {
            var raw = (targetRaw ?? "").Trim();
            var service = raw.Length == 0
                          || raw.Equals("sibling", StringComparison.OrdinalIgnoreCase)
                          || raw.Equals("service", StringComparison.OrdinalIgnoreCase)
                ? ServiceTarget
                : raw.Equals("self", StringComparison.OrdinalIgnoreCase)
                  || raw.Equals("here", StringComparison.OrdinalIgnoreCase)
                    ? selfRoot ?? ServiceTarget
                    : Path.GetFullPath(raw);
            return new TargetDecision(true, service, sibling, targetRaw, null, null);
        }

        var planProbe = Cdp.Deploy.CdpDeployPlanner.PlanInstallTarget(
            Cdp.Deploy.CdpDeployInstallRequest.ForResolve(
                Cdp.Deploy.CdpDeployModeParser.Parse(mode),
                selfRoot,
                targetRaw,
                force));

        if (!planProbe.Ok)
        {
            return new TargetDecision(
                false,
                planProbe.Plan?.BridgePublishTarget,
                sibling,
                targetRaw,
                planProbe.Error,
                planProbe.Hint);
        }

        return new TargetDecision(
            true,
            planProbe.Plan!.BridgePublishTarget,
            sibling,
            targetRaw,
            null,
            null);
    }
}
