#nullable enable

namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: resolve desk_detail / nav_detail (delegates to Modeling CdsRouting).</summary>
public sealed class DeskDetailUnit : ICockpitComputeUnit
{
    public readonly record struct Input(string? DeskDetailRaw, string? FocusId);

    public DeskDetailDecision Compute(in Input input) =>
        CdsRouting.resolveDeskDetail(input.DeskDetailRaw, input.FocusId);
}
