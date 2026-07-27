#nullable enable
using CdpMcp.Cockpit.Surface;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Agent IDE cockpit — seats desk + soft organs (ADR 0191/0193).
/// Legacy MFD aliases: go=sys|chk|gates; desk_detail=nav. <c>cmd=</c> REPL; <c>go=</c> places organ in seat.
/// Partials: Build/Pins/Git/Collect/Args/Models/SnapPanes/GoResult/Sys/Surface/…
/// </summary>
internal static partial class IdeCockpit
{
    public const string SchemaVersion = "cockpit/v1.20";
    public const int GoResultCapChars = 24_000;
    public const int GoPulseCapChars = 1_200;
    public const int MaxTiles = 4;

    /// <summary>Exposed for Tools → Options desk.default_layout choices.</summary>
    public static string[] LayoutPresetIds =>
        DeskLayouts.Ids
            .Concat(IdeDeskSeats.PresetIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsKnownGoVerb(string verb) => GoMap.ContainsKey(verb);
    public static bool IsKnownPinAlias(string alias) => DeskPins.Contains(alias);

    /// <summary>Canonical seat organ pin (aliases → plan/editor_scene/…).</summary>
    public static string CanonicalOrganPin(string organPin) => DeskPins.Canonical(organPin);

    static readonly DeskLayoutPresetCatalog DeskLayouts = new();
    static IReadOnlyDictionary<string, string[]> LayoutPresets => DeskLayouts.Map;

    static readonly DeskPinAliasCatalog DeskPins = new();
    static IReadOnlyDictionary<string, string> PinAliases => DeskPins.Map;
    static readonly DeskPlaceableOrganUnit DeskPlaceable = new();

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
