#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: whether BuildAsync can short-circuit world scene go= to a pulse pane.</summary>
public sealed class WorldSceneGoUnit : ICockpitComputeUnit
{
    public readonly record struct Input(
        string? GoVerb,
        string? GoDetail,
        bool HasGoArgs,
        bool IsWorldSceneGo);

    public readonly record struct Snapshot(bool UseWorldSnap, string? Pin) : ICockpitComputeUnitPayload;

    public Snapshot Compute(in Input input)
    {
        if (input.GoVerb is not { Length: > 0 })
            return new Snapshot(false, null);
        if (!input.IsWorldSceneGo)
            return new Snapshot(false, null);

        var detail = (input.GoDetail ?? "pulse").Trim().ToLowerInvariant();
        if (detail is "full" || input.HasGoArgs)
            return new Snapshot(false, null);

        return new Snapshot(true, input.GoVerb.Trim());
    }
}
