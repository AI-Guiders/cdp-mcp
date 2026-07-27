#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: does pane_full= target this seat/organ?</summary>
public sealed class SeatFullPaneMatchUnit : ICockpitComputeUnit
{
    public bool Matches(
        string? fullPane,
        string seatId,
        string organ,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (fullPane is not { Length: > 0 })
            return false;
        if (string.Equals(fullPane, organ, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(fullPane, seatId, StringComparison.OrdinalIgnoreCase))
            return true;
        return aliases is not null
            && aliases.TryGetValue(fullPane, out var alias)
            && string.Equals(alias, organ, StringComparison.OrdinalIgnoreCase);
    }
}
