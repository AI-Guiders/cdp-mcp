#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: non-soft-organ seat pin → world/editor/script snap (else dispatch).</summary>
public sealed class SeatFallbackSnapUnit : ICockpitComputeUnit
{
    public enum SnapKind
    {
        None,
        World,
        Editor,
        Script
    }

    public readonly record struct Input(string PlanPin, bool WantFull, bool IsWorldOrgan);

    public SnapKind Classify(in Input input)
    {
        if (input.WantFull)
            return SnapKind.None;

        if (input.IsWorldOrgan)
            return SnapKind.World;

        return input.PlanPin switch
        {
            "editor_scene" or "buffer_scene" or "editor" or "buffer" => SnapKind.Editor,
            "script_scene" or "script" or "probe" => SnapKind.Script,
            _ => SnapKind.None
        };
    }
}
