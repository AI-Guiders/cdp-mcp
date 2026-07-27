#nullable enable
using System.Text.Json;

namespace CdpMcp.Cockpit.Surface;

/// <summary>Strip cockpit steer keys before organ dispatch from a seat pane.</summary>
public sealed class SeatOrganArgsSanitizer
{
    static readonly string[] SteerKeys =
    [
        "go", "do", "cmd", "line", "repl", "go_args", "tm_op",
        "seat", "organ", "pin", "layout", "pins", "tiles",
        "pane_full", "full_pane", "seats_detail", "view_detail",
        "desk_detail", "nav_detail", "locus", "focus", "mfd", "page",
        "pin_clear", "clear_pins", "seat_clear", "clear_seats"
    ];

    public Dictionary<string, JsonElement> Sanitize(
        IReadOnlyDictionary<string, JsonElement> args,
        bool wantFull)
    {
        var tileArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
            tileArgs[kv.Key] = kv.Value;
        tileArgs["go_detail"] = JsonSerializer.SerializeToElement(wantFull ? "full" : "pulse");
        foreach (var key in SteerKeys)
            tileArgs.Remove(key);
        return tileArgs;
    }
}
