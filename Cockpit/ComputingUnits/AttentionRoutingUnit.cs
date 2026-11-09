#nullable enable

namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: normalize MFD/go attention before channel dispatch (delegates to Modeling CdsRouting).</summary>
public sealed class AttentionRoutingUnit : ICockpitComputeUnit
{
    public AttentionRoutingDecision Compute(in AttentionRoutingInput input) =>
        CdsRouting.route(input);
}
