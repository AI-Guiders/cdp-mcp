using System.Text.Json;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;
internal static partial class IdeDeskSeats
{
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
            if (layout is { Length: > 0 } && SeatPresets.TryGetValue(layout.Trim(), out var map))
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

        foreach (var(seat, pin)in map)
        {
            if (Order.Contains(seat, StringComparer.OrdinalIgnoreCase))
                Sticky[seat] = CanonicalOrganPin(pin);
        }
    }
}