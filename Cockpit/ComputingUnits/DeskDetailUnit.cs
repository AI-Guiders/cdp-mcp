#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: resolve desk_detail / nav_detail (ADR 0097).</summary>
public sealed class DeskDetailUnit : ICockpitComputeUnit
{
    public readonly record struct Input(string? DeskDetailRaw, string? FocusId);

    public readonly record struct Snapshot(string DeskDetail, bool WantNav) : ICockpitComputeUnitPayload;

    public Snapshot Compute(in Input input)
    {
        var raw = (input.DeskDetailRaw ?? "slim").Trim().ToLowerInvariant();
        if (raw is "compact")
            raw = "slim";
        if (input.FocusId is { Length: > 0 } && raw is "slim" or "omit")
            raw = "nav";
        if (raw is "omit")
            raw = "slim";
        if (raw is not ("slim" or "nav" or "full"))
            raw = "slim";
        return new Snapshot(raw, raw is "nav" or "full");
    }
}
