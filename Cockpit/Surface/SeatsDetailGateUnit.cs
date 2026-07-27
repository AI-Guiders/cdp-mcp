#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: seats_detail / W-spray refuse (ADR 0036 desk surface).</summary>
public sealed class SeatsDetailGateUnit : ICockpitComputeUnit
{
    public readonly record struct Input(
        string? SeatsDetailRaw,
        string? FullPane,
        bool SeatsPanesFlag,
        bool CompactDefaultTrue);

    public readonly record struct Snapshot(
        string SeatsDetail,
        bool WantPanes,
        string? ThrashNote) : ICockpitComputeUnitPayload;

    public Snapshot Compute(in Input input)
    {
        var seatsDetail = (input.SeatsDetailRaw ?? "compact").Trim().ToLowerInvariant();
        string? thrashNote = null;
        if ((seatsDetail is "full" or "panes") && input.FullPane is not { Length: > 0 })
        {
            thrashNote =
                "W-spray refused: seats_detail=full needs pane_full=<seat|organ>; using compact (A).";
            seatsDetail = "compact";
        }

        var wantPanes = seatsDetail is "full" or "panes"
            || input.SeatsPanesFlag
            || input.CompactDefaultTrue == false;

        return new Snapshot(seatsDetail, wantPanes, thrashNote);
    }
}
