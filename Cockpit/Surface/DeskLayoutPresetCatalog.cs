#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: legacy tile layout presets (layout= → pin list).</summary>
public sealed class DeskLayoutPresetCatalog : ICockpitComputeUnit
{
    static readonly Dictionary<string, string[]> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code+net"] = ["editor_scene", "browser"],
        ["code+shell"] = ["editor_scene", "shell"],
        ["code+git"] = ["editor_scene", "git_scene"],
        ["net+shell"] = ["browser", "shell"],
        ["desk"] = ["editor_scene", "browser", "shell"],
        ["cockpit"] = ["editor_scene", "browser", "shell"],
        ["code+net+shell"] = ["editor_scene", "browser", "shell"],
        ["agent"] = ["plan", "editor_scene", "script_scene"],
    };

    public IReadOnlyDictionary<string, string[]> Map => BuiltIns;

    public IEnumerable<string> Ids => BuiltIns.Keys;

    public bool TryGet(string layout, out string[] pins) =>
        BuiltIns.TryGetValue(layout, out pins!);
}
