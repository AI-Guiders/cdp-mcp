using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Spatial desk render for Scan Pattern seats — one composition, not three JSON blobs (ADR 0191).
/// Partials: Lines (pane→line), Humanize (pulse), Render (banner/ascii).
/// </summary>
internal static partial class IdeDeskView
{
    public const string SchemaVersion = "desk_view/v1";

    public readonly record struct Slot(
        string Seat,
        string? Organ,
        bool Empty,
        bool Ok,
        string Line,
        bool Full);

    public static object Build(IReadOnlyList<Slot> slots)
    {
        var banner = BuildBanner(slots);
        var board = slots.Select(FormatBoardLine).ToArray();
        var ascii = BuildAscii(slots);
        return new
        {
            schema = SchemaVersion,
            scan = "p→forward→m",
            banner,
            board,
            ascii,
            hint = "Scan banner/board first. pane_full=<seat|organ> for one dump; seats_detail=full alone is W-spray (refused)."
        };
    }

    public static string ShortOrgan(string? organ)
    {
        if (string.IsNullOrWhiteSpace(organ)) return "—";
        var o = organ.Trim();
        if (o.EndsWith("_scene", StringComparison.OrdinalIgnoreCase))
            o = o[..^"_scene".Length];
        return o.ToLowerInvariant() switch
        {
            "editor" => "editor",
            "buffer" => "buffer",
            "project" => "project",
            "git" => "git",
            "shell" => "shell",
            "browser" or "internet_browser" or "scene_internet_browser" => "browser",
            "mcp" => "mcp",
            "settings" or "options" or "prefs" => "options",
            "correspondence" or "corr" => "corr",
            "test" => "test",
            "debug" => "debug",
            "work" or "tasks" or "plan" or "feature" or "task" or "tm" => "plan",
            "report" or "evidence" or "pfd" => "report",
            "alert" or "eicas" or "sa" => "alert",
            "problems" or "problem" or "errlist" or "errorlist" or "err" or "diags" => "problems",
            "plugins" or "plugin" or "vsix" => "plugins",
            "quality" or "gates" => "gates",
            "sys" => "sys",
            "ecl" or "chk" => "ecl",
            "qrh" or "eqrh" or "handbook" => "qrh",
            "review" => "review",
            "analysis" => "analysis",
            "script" or "probe" => "script",
            "semantic_map" or "semantic" => "semantic",
            _ => o.Length <= 12 ? o : o[..11] + "…"
        };
    }

    public static string SeatGlyph(string seat) => seat.ToLowerInvariant() switch
    {
        "p" => "P",
        "forward" => "F",
        "m" => "M",
        _ => seat.ToUpperInvariant()
    };

    public static string SeatTitle(string seat) => seat.ToLowerInvariant() switch
    {
        "p" => "P (PFD)",
        "forward" => "Forward",
        "m" => "M (MFD)",
        _ => seat
    };
}
