#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: optional edit:sniper locus when corridor hold is armed.</summary>
public sealed class DeskSniperLocusUnit : ICockpitComputeUnit
{
    public readonly record struct Input(bool HasHold, bool IsArmed, string Pulse, object HoldCard);

    public FocusLocusUnit.LocusRef? TryBuild(in Input input)
    {
        if (!input.HasHold)
            return null;
        var next = input.IsArmed
            ? "armed → go=put_sniper | paste_sniper | scope_clear"
            : "go=scope → lock+auto-arm (fire hard-blocked until armed)";
        return new FocusLocusUnit.LocusRef(
            "edit:sniper",
            "sniper",
            $"aim {input.Pulse}",
            next,
            input.IsArmed ? "put_sniper" : "scope",
            input.HoldCard);
    }
}
