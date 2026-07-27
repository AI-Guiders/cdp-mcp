#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: editor/buffer pulse pane from buffer snap counts.</summary>
public sealed class EditorSnapPaneUnit : ICockpitComputeUnit
{
    public readonly record struct BufferCounts(int Count, int DirtyCount, int DiskChangedCount);

    public object Build(in BufferCounts buffer)
    {
        var pulse = buffer.Count == 0
            ? "—"
            : buffer.DiskChangedCount > 0
                ? $"{buffer.Count} buf · disk×{buffer.DiskChangedCount}"
                : buffer.DirtyCount > 0
                    ? $"{buffer.Count} buf · dirty×{buffer.DirtyCount}"
                    : $"{buffer.Count} buf";
        return new
        {
            ok = true,
            go = "editor_scene",
            detail = "pulse",
            pulse,
            snap = true,
            hint = "pane_full=editor for dump"
        };
    }
}
