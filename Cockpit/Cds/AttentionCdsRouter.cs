#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Cds;

/// <summary>CDS router over AttentionRoutingUnit (ADR 0036 + 0097).</summary>
public sealed class AttentionCdsRouter : ICdsRouter<AttentionRoutingInput, AttentionRoutingDecision>
{
    readonly AttentionRoutingUnit _unit = new();

    public AttentionRoutingDecision Route(AttentionRoutingInput input) =>
        _unit.Compute(in input);
}
