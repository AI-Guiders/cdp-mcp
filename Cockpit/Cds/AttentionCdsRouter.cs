#nullable enable
using CdpMcp.Cockpit.Cds;
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Cds;

/// <summary>CDS router over AttentionRoutingUnit (ADR 0036 + 0097).</summary>
public sealed class AttentionCdsRouter : ICdsRouter<AttentionRoutingUnit.Input, AttentionRoutingUnit.Snapshot>
{
    readonly AttentionRoutingUnit _unit = new();

    public AttentionRoutingUnit.Snapshot Route(AttentionRoutingUnit.Input input) =>
        _unit.Compute(in input);
}
