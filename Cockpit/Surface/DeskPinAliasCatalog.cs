#nullable enable
using CdpMcp.Cockpit.ComputingUnits;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface CCU: seat/organ pin aliases → canonical organ pin.</summary>
public sealed class DeskPinAliasCatalog : ICockpitComputeUnit
{
    static readonly Dictionary<string, string> BuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor"] = "editor_scene",
        ["editor_scene"] = "editor_scene",
        ["code"] = "editor_scene",
        ["buffer"] = "buffer_scene",
        ["buffer_scene"] = "buffer_scene",
        ["browser"] = "browser",
        ["net"] = "browser",
        ["internet"] = "browser",
        ["internet_browser"] = "browser",
        ["scene_internet_browser"] = "browser",
        ["shell"] = "shell_scene",
        ["shell_scene"] = "shell_scene",
        ["git"] = "git_scene",
        ["git_scene"] = "git_scene",
        ["debug"] = "debug_scene",
        ["debug_scene"] = "debug_scene",
        ["test"] = "test_scene",
        ["test_scene"] = "test_scene",
        ["mcp"] = "mcp_scene",
        ["mcp_scene"] = "mcp_scene",
        ["settings"] = "settings",
        ["settings_scene"] = "settings",
        ["ide_settings"] = "settings",
        ["prefs"] = "settings",
        ["options"] = "settings",
        ["correspondence"] = "correspondence",
        ["corr"] = "correspondence",
        ["work"] = "plan",
        ["tasks"] = "plan",
        ["plan"] = "plan",
        ["task"] = "plan",
        ["feature"] = "plan",
        ["tm"] = "plan",
        ["report"] = "report",
        ["evidence"] = "report",
        ["pfd"] = "report",
        ["find_desk"] = "find_desk",
        ["search_desk"] = "find_desk",
        ["code_search"] = "find_desk",
        ["sa_desk"] = "sa_desk",
        ["code_sa"] = "sa_desk",
        ["pre_sa"] = "sa_desk",
        ["sa_code"] = "sa_desk",
        ["refactor_plan"] = "refactor_plan",
        ["refactor"] = "refactor_plan",
        ["cdp_refactor"] = "refactor_plan",
        ["debt_scene"] = "refactor_plan",
        ["debug_desk"] = "debug_desk",
        ["dap_sa"] = "debug_desk",
        ["debug_sa"] = "debug_desk",
        ["test_desk"] = "test_desk",
        ["test_sa"] = "test_desk",
        ["build_desk"] = "build_desk",
        ["ship_desk"] = "build_desk",
        ["build_sa"] = "build_desk",
        ["ship_sa"] = "build_desk",
        ["crm"] = "crm",
        ["callout"] = "crm",
        ["crm_panel"] = "crm",
        ["files_desk"] = "files_desk",
        ["files"] = "files_desk",
        ["explorer"] = "files_desk",
        ["fm"] = "files_desk",
        ["file_manager"] = "files_desk",
        ["ignite_desk"] = "ignite_desk",
        ["ignite"] = "ignite_desk",
        ["autoignite"] = "ignite_desk",
        ["cdt_ignite"] = "ignite_desk",
        ["webcam_desk"] = "webcam_desk",
        ["webcam"] = "webcam_desk",
        ["camera"] = "webcam_desk",
        ["sense"] = "webcam_desk",
        ["pressure_desk"] = "pressure_desk",
        ["pressure"] = "pressure_desk",
        ["compact_prep"] = "pressure_desk",
        ["pre_compact"] = "pressure_desk",
        ["alert"] = "alert",
        ["eicas"] = "alert",
        ["sa"] = "alert",
        ["ecl"] = "ecl",
        ["chk"] = "ecl",
        ["qrh"] = "qrh",
        ["eqrh"] = "qrh",
        ["handbook"] = "qrh",
        ["review"] = "review",
        ["problems"] = "problems",
        ["problem"] = "problems",
        ["errlist"] = "problems",
        ["errorlist"] = "problems",
        ["err"] = "problems",
        ["diags"] = "problems",
        ["plugins"] = "plugins",
        ["plugin"] = "plugins",
        ["vsix"] = "plugins",
        ["project"] = "project_scene",
        ["project_scene"] = "project_scene",
    };

    public IReadOnlyDictionary<string, string> Map => BuiltIns;

    public bool Contains(string alias) => BuiltIns.ContainsKey(alias);

    public bool TryCanonical(string alias, out string canonical) =>
        BuiltIns.TryGetValue(alias, out canonical!);

    public string Canonical(string organPin)
    {
        var pin = organPin.Trim().ToLowerInvariant();
        return BuiltIns.TryGetValue(pin, out var canon) ? canon : pin;
    }
}
