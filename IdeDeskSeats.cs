using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Scan-pattern desk seats (ADR 0191 / 0021): fixed <c>P | Forward | M</c>.
/// Organ open → replace-in-seat. Sticky map survives MCP remount in WitDB <c>desk_seats</c>.
/// </summary>
internal static partial class IdeDeskSeats
{
    public const string SchemaRole = "seats";
    public static readonly string[] Order = ["p", "forward", "m"];

    static readonly object Gate = new();
    static bool Hydrated;
    static IntentWorkspaceStore? Store;
    static readonly Dictionary<string, string?> Sticky = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p"] = null,
        ["forward"] = null,
        ["m"] = null,
    };

    static readonly Dictionary<string, string> DefaultPolicy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["editor_scene"] = "forward",
        ["editor"] = "forward",
        ["buffer_scene"] = "forward",
        ["buffer"] = "forward",
        ["edit_draft"] = "forward",
        ["edit_plan"] = "forward",
        ["scope"] = "forward",
        ["target"] = "forward",
        ["peek"] = "forward",
        ["sniper"] = "forward",
        ["script_scene"] = "m",
        ["script"] = "m",
        ["script_put"] = "m",
        ["script_open"] = "m",
        ["script_check"] = "m",
        ["script_run"] = "m",
        ["script_last"] = "m",
        ["probe"] = "m",
        ["ps1_scene"] = "m",
        ["ps1"] = "m",
        ["ise"] = "m",
        ["ps1_desk"] = "m",
        ["ps1_put"] = "m",
        ["ps1_open"] = "m",
        ["ps1_check"] = "m",
        ["ps1_run"] = "m",
        ["ps1_last"] = "m",
        ["ps1_help"] = "m",
        ["cdp_ps1_scene"] = "m",
        ["project_scene"] = "p",
        ["project"] = "p",
        ["work"] = "p",
        ["plan"] = "p",
        ["tasks"] = "p",
        ["tm"] = "p",
        ["feature"] = "p",
        ["task"] = "p",
        ["report"] = "p",
        ["evidence"] = "p",
        ["pfd"] = "p",
        ["find_desk"] = "p",
        ["search_desk"] = "p",
        ["code_search"] = "p",
        ["sa_desk"] = "p",
        ["code_sa"] = "p",
        ["pre_sa"] = "p",
        ["sa_code"] = "p",
        ["debug_desk"] = "p",
        ["dap_sa"] = "p",
        ["debug_sa"] = "p",
        ["test_desk"] = "p",
        ["test_sa"] = "p",
        ["build_desk"] = "p",
        ["ship_desk"] = "p",
        ["build_sa"] = "p",
        ["ship_sa"] = "p",
        ["crm"] = "p",
        ["callout"] = "p",
        ["crm_panel"] = "p",
        ["files_desk"] = "p",
        ["files"] = "p",
        ["explorer"] = "p",
        ["fm"] = "p",
        ["file_manager"] = "p",
        ["md_author"] = "p",
        ["md_author_desk"] = "p",
        ["markdown_author"] = "p",
        ["md_include"] = "p",
        ["cdp_md_author"] = "p",
        ["ignite_desk"] = "p",
        ["ignite"] = "p",
        ["autoignite"] = "p",
        ["cdt_ignite"] = "p",
        ["webcam_desk"] = "p",
        ["pressure_desk"] = "p",
        ["pressure"] = "p",
        ["domain"] = "p",
        ["domain_desk"] = "p",
        ["ownership"] = "p",
        ["cdp_domain"] = "p",
        ["compact_prep"] = "p",
        ["pre_compact"] = "p",
        ["webcam"] = "p",
        ["camera"] = "p",
        ["sense"] = "p",
        ["alert"] = "p",
        ["eicas"] = "p",
        ["sa"] = "p",
        ["quality"] = "p",
        ["gates"] = "p",
        ["problems"] = "p",
        ["problem"] = "p",
        ["errlist"] = "p",
        ["errorlist"] = "p",
        ["err"] = "p",
        ["diags"] = "p",
        ["plugins"] = "p",
        ["plugin"] = "p",
        ["vsix"] = "p",
        ["sys"] = "m",
        ["ecl"] = "m",
        ["chk"] = "m",
        ["qrh"] = "m",
        ["eqrh"] = "m",
        ["handbook"] = "m",
        ["review"] = "p",
        ["debug_scene"] = "p",
        ["debug"] = "p",
        ["analysis_scene"] = "p",
        ["analysis"] = "p",
        ["browser"] = "m",
        ["scene_internet_browser"] = "m",
        ["internet_browser"] = "m",
        ["git_scene"] = "m",
        ["git"] = "m",
        ["shell_scene"] = "m",
        ["shell"] = "m",
        ["mcp_scene"] = "m",
        ["mcp"] = "m",
        ["settings"] = "m",
        ["options"] = "m",
        ["prefs"] = "m",
        ["correspondence"] = "m",
        ["corr"] = "m",
        ["semantic_map"] = "m",
        ["semantic"] = "m",
        ["test_scene"] = "m",
        ["test"] = "m",
        ["arch_desk"] = "m",
        ["arch_board"] = "m",
        ["arch"] = "m",
        ["board"] = "m",
        ["sketch_desk"] = "m",
        ["cdp_arch"] = "m",
        ["toolchain"] = "m",
        ["toolchain_desk"] = "m",
        ["toolchain_ensure"] = "m",
        ["toolchain_probe"] = "m",
        ["cdp_toolchain"] = "m",
        ["onboard_desk"] = "m",
        ["explore_desk"] = "m",
        ["onboard"] = "m",
        ["explore"] = "m",
        ["cdp_onboard"] = "m",
        ["restore"] = "m",
    };

    public static string[] PresetIds =>
        SeatPresets.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Wire WitDB store (call from EnsureWorkspaceDb).</summary>
    public static void Bind(IntentWorkspaceStore store) => Store = store;

    public static bool IsSeatsMode()
    {
        var mode = IdeSettingsHabitat.EffectiveDeskMode();
        return !mode.Equals("tiles", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string?> Snapshot()
    {
        lock (Gate)
            return Order.ToDictionary(s => s, s => Sticky[s], StringComparer.OrdinalIgnoreCase);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            foreach (var s in Order)
                Sticky[s] = null;
            Hydrated = true;
            PersistUnlocked();
        }
    }

    public static bool TryApplyPreset(string layoutId, bool merge = false)
    {
        if (!SeatPresets.TryGetValue(layoutId.Trim(), out var map))
            return false;
        lock (Gate)
        {
            ApplyPresetUnlocked(map, merge);
            PersistUnlocked();
        }

        return true;
    }

    /// <summary>
    /// Cold desk: hydrate from WitDB (survives remount), else layout/Options defaults.
    /// Explicit clear persists empty — remount keeps empty, does not re-default.
    /// </summary>
    public static void EnsureDefaultsFromSettings()
    {
        lock (Gate)
        {
            if (Order.Any(s => Sticky[s] is { Length: > 0 }))
                return;

            if (!Hydrated)
            {
                Hydrated = true;
                if (TryLoadUnlocked())
                    return;
            }
            else
                return;

            var layout = IdeSettingsHabitat.EffectiveDeskLayout();
            if (layout is { Length: > 0 }
                && SeatPresets.TryGetValue(layout.Trim(), out var map))
            {
                ApplyPresetUnlocked(map, merge: false);
                PersistUnlocked();
                return;
            }

            PlaceUnlocked("p", IdeSettingsHabitat.EffectiveSeatDefault("p"));
            PlaceUnlocked("forward", IdeSettingsHabitat.EffectiveSeatDefault("forward"));
            PlaceUnlocked("m", IdeSettingsHabitat.EffectiveSeatDefault("m"));
            PersistUnlocked();
        }
    }

    static void ApplyPresetUnlocked(Dictionary<string, string> map, bool merge)
    {
        if (!merge)
        {
            foreach (var s in Order)
                Sticky[s] = null;
        }

        foreach (var (seat, pin) in map)
        {
            if (Order.Contains(seat, StringComparer.OrdinalIgnoreCase))
                Sticky[seat] = CanonicalOrganPin(pin);
        }
    }
}
