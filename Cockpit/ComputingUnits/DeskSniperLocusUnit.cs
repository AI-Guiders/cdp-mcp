#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: optional edit:sniper locus when corridor hold is armed.</summary>
public sealed class DeskSniperLocusUnit : ICockpitComputeUnit
{
    public readonly record struct Input(bool HasHold, string Pulse, object HoldCard);

    public FocusLocusUnit.LocusRef? TryBuild(in Input input)
    {
        if (!input.HasHold)
            return null;
        return new FocusLocusUnit.LocusRef(
            "edit:sniper",
            "sniper",
            $"aim {input.Pulse}",
            "go=target → go=edit_draft | go=scope_clear",
            "target",
            input.HoldCard);
    }
}
