using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Scan-pattern desk seats (ADR 0191 / 0021): fixed <c>P | Forward | M</c>.
/// Organ open → replace-in-seat. Sticky map survives MCP remount in WitDB <c>desk_seats</c>.
/// </summary>
internal static class IdeDeskSeats
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

    /// <summary>layout id → seat → pin (canonical go verb).</summary>
    static readonly Dictionary<string, Dictionary<string, string>> SeatPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cockpit"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["desk"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["code+net"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["code+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["code+git"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
            ["net+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "browser",
                ["m"] = "shell_scene",
            },
            ["code+net+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "shell_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["agent"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "script_scene",
            },
            ["bug"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "problems",
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["verify"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "ecl",
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["phase-review"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "review",
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
            ["phase-explore"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["phase-handoff"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
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

    public static string? ResolveSeatForOrgan(string organPin)
    {
        var pin = CanonicalOrganPin(organPin);
        foreach (var key in PolicyOverrideKeys(pin))
        {
            var over = IdeSettingsStore.GetOrNull($"desk.seat.organ.{key}");
            if (over is { Length: > 0 } && NormalizeSeatId(over) is { } s)
                return s;
        }

        if (DefaultPolicy.TryGetValue(pin, out var seat))
            return seat;

        return "m";
    }

    static IEnumerable<string> PolicyOverrideKeys(string pin)
    {
        yield return pin;
        if (pin.EndsWith("_scene", StringComparison.OrdinalIgnoreCase))
            yield return pin[..^"_scene".Length];
    }

    public static string CanonicalOrganPin(string organPin) =>
        IdeCockpit.CanonicalOrganPin(organPin);

    public static string? PlaceOrgan(string organPin)
    {
        var pin = CanonicalOrganPin(organPin);
        if (pin.Length == 0) return null;
        var seat = ResolveSeatForOrgan(pin);
        if (seat is null) return null;
        lock (Gate)
        {
            PlaceUnlocked(seat, pin);
            PersistUnlocked();
        }

        return seat;
    }

    public static bool TryPlaceExplicit(string seatRaw, string organPin)
    {
        var seat = NormalizeSeatId(seatRaw);
        if (seat is null || string.IsNullOrWhiteSpace(organPin))
            return false;
        lock (Gate)
        {
            PlaceUnlocked(seat, CanonicalOrganPin(organPin));
            PersistUnlocked();
        }

        return true;
    }

    public static string? NormalizeSeatId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "p" or "pfd" or "left" => "p",
            "f" or "fwd" or "forward" or "centre" or "center" or "main" => "forward",
            "m" or "mfd" or "right" => "m",
            _ => null
        };
    }

    static void PlaceUnlocked(string seat, string? pin)
    {
        if (!Sticky.ContainsKey(seat)) return;
        Sticky[seat] = string.IsNullOrWhiteSpace(pin) ? null : CanonicalOrganPin(pin);
    }

    static bool TryLoadUnlocked()
    {
        if (Store is null)
            return false;
        try
        {
            if (!Store.DeskSeatsTryLoad(Sticky))
                return false;
            foreach (var seat in Order)
            {
                if (Sticky.TryGetValue(seat, out var pin) && pin is { Length: > 0 })
                    Sticky[seat] = CanonicalOrganPin(pin);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    static void PersistUnlocked()
    {
        if (Store is null)
            return;
        try
        {
            Store.DeskSeatsSave(Order.ToDictionary(s => s, s => Sticky[s], StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            // Desk must not die on IO; next mutation retries.
        }
    }

    public static object Card(IReadOnlyList<object> slots, IReadOnlyList<object>? panes, object? view = null)
    {
        // Root cockpit already carries view — omit duplicate unless caller embeds.
        if (view is null)
        {
            return new
            {
                ok = true,
                role = SchemaRole,
                scan = "p→forward→m",
                order = Order,
                map = Snapshot(),
                persist = "witdb:desk_seats",
                slots,
                panes,
                count = slots.Count,
                hint =
                    "Read root view.banner / view.ascii. Seats sticky in WitDB. " +
                    "panes when seats_detail=full or pane_full=. go=browser → M replaces."
            };
        }

        return new
        {
            ok = true,
            role = SchemaRole,
            scan = "p→forward→m",
            order = Order,
            map = Snapshot(),
            persist = "witdb:desk_seats",
            view,
            slots,
            panes,
            count = slots.Count,
            hint =
                "Read view.banner / view.ascii. Seats sticky in WitDB (survive remount). " +
                "panes when seats_detail=full or pane_full=. go=browser → M replaces."
        };
    }

    public static bool TryParseSeatAssignment(IReadOnlyDictionary<string, JsonElement> args, out string? seat, out string? organ)
    {
        seat = NormalizeSeatId(Opt(args, "seat"));
        organ = Opt(args, "organ") ?? Opt(args, "pin");
        if (seat is null || organ is null || organ.Length == 0)
            return false;
        return true;
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
