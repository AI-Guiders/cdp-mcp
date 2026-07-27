#nullable enable
namespace CdpMcp.Cockpit.ComputingUnits;

/// <summary>CCU: build desk focus card from locus id + loci list.</summary>
public sealed class FocusLocusUnit : ICockpitComputeUnit
{
    public readonly record struct LocusRef(
        string Id,
        string Kind,
        string Pulse,
        string Drill,
        string? Go,
        object? Detail);

    public object? Build(string? focusId, IReadOnlyList<LocusRef> loci)
    {
        if (string.IsNullOrWhiteSpace(focusId))
            return null;

        foreach (var hit in loci)
        {
            if (!string.Equals(hit.Id, focusId, StringComparison.OrdinalIgnoreCase))
                continue;
            return new
            {
                ok = true,
                locus = hit.Id,
                kind = hit.Kind,
                pulse = hit.Pulse,
                drill = hit.Drill,
                go = hit.Go,
                detail = hit.Detail
            };
        }

        return new
        {
            ok = false,
            locus = focusId,
            reason = "unknown_locus",
            hint = "Pick id from loci[]."
        };
    }
}
