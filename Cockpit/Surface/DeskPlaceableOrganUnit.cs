#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: can this pin own a seat/tile pulse (not one-shot go=).</summary>
public sealed class DeskPlaceableOrganUnit : ICockpitComputeUnit
{
    public bool IsPlaceable(string pin, IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (aliases is not null && aliases.ContainsKey(pin))
            return true;

        // Scene-like go verbs that own a seat pulse (not clipboard / find one-shots).
        return pin is "editor_scene" or "buffer_scene" or "browser" or "shell_scene" or "git_scene"
            or "debug_scene" or "test_scene" or "mcp_scene" or "settings" or "project_scene"
            or "plan" or "work" or "report" or "evidence" or "pfd" or "alert" or "eicas" or "sa"
            or "pressure_desk" or "pressure" or "compact_prep" or "pre_compact"
            or "problems" or "plugins"
            or "correspondence" or "quality" or "gates" or "sys" or "chk" or "ecl" or "analysis_scene"
            or "script_scene" or "semantic_map";
    }
}
