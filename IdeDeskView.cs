using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Spatial desk render for Scan Pattern seats — one composition, not three JSON blobs (ADR 0191).
/// </summary>
internal static class IdeDeskView
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
            hint = "Scan banner/board first. pane_full=<seat|organ> or seats_detail=full for organ dump."
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
            "chk" => "chk",
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

    public static (bool Ok, string Line) LineFromPane(object? pane, bool empty, string? organ)
    {
        if (empty || organ is null)
            return (true, "(empty)");

        if (pane is null)
            return (true, ShortOrgan(organ));

        try
        {
            var json = JsonSerializer.Serialize(pane);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return LineFromJson(root, organ);
        }
        catch
        {
            return (true, ShortOrgan(organ));
        }
    }

    static (bool Ok, string Line) LineFromJson(JsonElement root, string? organ)
    {
        var ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;

        // Full organ dump: prefer nested result.pulse (Plan Feature › Task).
        if (root.TryGetProperty("detail", out var detail)
            && detail.ValueKind == JsonValueKind.String
            && detail.GetString() == "full"
            && root.TryGetProperty("result", out var nested)
            && nested.ValueKind == JsonValueKind.Object)
        {
            var dig = LineFromJson(nested, organ);
            if (!string.IsNullOrWhiteSpace(dig.Line)
                && dig.Line != ShortOrgan(organ)
                && !dig.Line.EndsWith(" · full", StringComparison.Ordinal))
                return dig;
            return (ok, ShortOrgan(organ) + " · full");
        }

        if (root.TryGetProperty("pulse", out var pulse) && pulse.ValueKind == JsonValueKind.String)
        {
            var p = pulse.GetString() ?? "";
            if (root.TryGetProperty("view", out var view)
                && view.ValueKind == JsonValueKind.Object
                && view.TryGetProperty("board", out var board)
                && board.ValueKind == JsonValueKind.Array
                && board.GetArrayLength() > 0)
            {
                // Prefer Feature › Task pulse over raw board title when present.
                if (p.Length > 0)
                    return (ok, HumanizePulse(p, organ));
                var first = board[0].GetString();
                if (!string.IsNullOrWhiteSpace(first))
                    return (ok, HumanizePulse(first, organ));
            }

            if (p.Length > 0)
                return (ok, HumanizePulse(p, organ));
        }

        if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return SoftErrorLine(err.GetString() ?? "error", organ);

        return (ok, ShortOrgan(organ));
    }

    /// <summary>Board-friendly pulse: drop schema noise.</summary>
    public static string HumanizePulse(string raw, string? organ)
    {
        var s = TrimLine(raw, 96);

        // correspondence/v0 FAIL path_required → need path
        if (s.Contains(" FAIL ", StringComparison.Ordinal)
            || s.Contains("FAIL ", StringComparison.Ordinal))
        {
            if (s.Contains("path_required", StringComparison.OrdinalIgnoreCase)
                || s.Contains("no project", StringComparison.OrdinalIgnoreCase)
                || s.Contains("workspace_path", StringComparison.OrdinalIgnoreCase))
                return "need cdp_open";

            var failAt = s.IndexOf("FAIL", StringComparison.Ordinal);
            if (failAt >= 0)
            {
                var reason = s[(failAt + 4)..].Trim().TrimStart(':').Trim();
                if (reason.Length > 0)
                    return TrimLine("fail " + reason, 40);
            }

            return "fail";
        }

        // editor_scene/v0 ok n=0 dirty=0 disk=0 → 0 buf
        if (s.Contains("/v", StringComparison.Ordinal) && s.Contains(" ok ", StringComparison.Ordinal))
        {
            var okAt = s.IndexOf(" ok ", StringComparison.Ordinal);
            if (okAt >= 0)
            {
                var rest = s[(okAt + 4)..].Trim();
                if (rest.Length > 0)
                    return TrimLine(HumanizeCounts(rest), 56);
            }
        }

        if (s.Contains("n=", StringComparison.Ordinal) && s.Contains("dirty=", StringComparison.Ordinal))
            return TrimLine(HumanizeCounts(s), 56);

        if (s.StartsWith("internet_browser", StringComparison.OrdinalIgnoreCase))
        {
            if (s.Contains("tabs=", StringComparison.Ordinal))
            {
                var i = s.IndexOf("tabs=", StringComparison.Ordinal);
                return TrimLine(s[i..], 40);
            }

            return "idle";
        }

        if (s.Contains("no project", StringComparison.OrdinalIgnoreCase)
            || s.Contains("no_project", StringComparison.OrdinalIgnoreCase))
            return "no project — cdp_open";

        if (s.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            && ShortOrgan(organ) == "git")
            return "need cdp_open";

        return TrimLine(s, 56);
    }

    /// <summary>n=0 dirty=0 disk=0 → 0 buf; n=3 dirty=1 → 3 buf ·1dirty</summary>
    public static string HumanizeCounts(string rest)
    {
        static int? Grab(string s, string key)
        {
            var i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var start = i + key.Length;
            var end = start;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end == start) return null;
            return int.TryParse(s[start..end], out var n) ? n : null;
        }

        var n = Grab(rest, "n=");
        var dirty = Grab(rest, "dirty=");
        if (n is null) return rest;
        if (n == 0)
            return "—";

        var line = $"{n} buf";
        if (dirty is > 0)
            line += $" ·{dirty}dirty";
        return line;
    }

    public static (bool Ok, string Line) SoftErrorLine(string error, string? organ)
    {
        // Cold project_scene without open → Access denied Application Data (noise)
        if (error.Contains("Application Data", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Access to the path", StringComparison.OrdinalIgnoreCase))
            return (true, "no project — cdp_open");

        if (error.Contains("workspace_path is required", StringComparison.OrdinalIgnoreCase)
            || error.Contains("path_required", StringComparison.OrdinalIgnoreCase)
            || error.Contains("path is required", StringComparison.OrdinalIgnoreCase))
            return (true, "need cdp_open");

        return (false, "err: " + TrimLine(error, 48));
    }

    /// <summary>Organs that thrash without <c>cdp_open</c> — synthesize quiet pulse instead of dispatch.</summary>
    public static bool OrganNeedsProject(string? organ)
    {
        if (string.IsNullOrWhiteSpace(organ)) return false;
        var o = organ.Trim().ToLowerInvariant();
        if (o.EndsWith("_scene", StringComparison.Ordinal))
            o = o[..^"_scene".Length];
        return o is "project" or "git" or "correspondence" or "corr"
            or "analysis" or "semantic" or "semantic_map" or "clones"
            or "test" or "debug" or "quality" or "gates"
            or "editor" or "browser" or "shell" or "mcp";
    }

    static string BuildBanner(IReadOnlyList<Slot> slots)
    {
        var parts = slots.Select(s =>
        {
            var g = SeatGlyph(s.Seat);
            if (s.Empty) return $"{g}:—";
            var mark = s.Ok ? "" : "!";
            return $"{g}:{mark}{ShortOrgan(s.Organ)}";
        });
        return "| " + string.Join(" | ", parts) + " |";
    }

    static string FormatBoardLine(Slot s)
    {
        var g = SeatGlyph(s.Seat).PadRight(1);
        var organ = (s.Empty ? "—" : ShortOrgan(s.Organ)).PadRight(10);
        var flag = s.Empty ? " " : (s.Ok ? "·" : "!");
        return $"{g}  {organ} {flag} {s.Line}";
    }

    static string BuildAscii(IReadOnlyList<Slot> slots)
    {
        // Fixed three columns — Scan Pattern on one “screen”.
        const int col = 22;
        var titles = new[] { "P", "Forward", "M" };
        var bySeat = slots.ToDictionary(s => s.Seat, StringComparer.OrdinalIgnoreCase);

        string Cell(string seat, int row)
        {
            if (!bySeat.TryGetValue(seat, out var s))
                return Pad("", col);
            if (row == 0)
                return Pad(s.Empty ? "(empty)" : ShortOrgan(s.Organ), col);
            if (row == 1)
                return Pad(s.Empty ? "" : TrimLine(s.Line, col - 1), col);
            return Pad(s.Full ? "[full]" : (s.Ok || s.Empty ? "" : "!"), col);
        }

        var sb = new StringBuilder();
        sb.Append('┌').Append(H(col)).Append('┬').Append(H(col)).Append('┬').Append(H(col)).Append('┐').Append('\n');
        sb.Append('│').Append(Pad(titles[0], col)).Append('│').Append(Pad(titles[1], col)).Append('│').Append(Pad(titles[2], col)).Append('│').Append('\n');
        sb.Append('├').Append(H(col)).Append('┼').Append(H(col)).Append('┼').Append(H(col)).Append('┤').Append('\n');
        for (var row = 0; row < 2; row++)
        {
            sb.Append('│').Append(Cell("p", row)).Append('│').Append(Cell("forward", row)).Append('│').Append(Cell("m", row)).Append('│').Append('\n');
        }

        sb.Append('└').Append(H(col)).Append('┴').Append(H(col)).Append('┴').Append(H(col)).Append('┘');
        return sb.ToString();

        static string H(int n) => new string('─', n);
        static string Pad(string s, int n)
        {
            s ??= "";
            if (s.Length > n) return s[..(n - 1)] + "…";
            return s.PadRight(n);
        }
    }

    static string TrimLine(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        if (s.Length <= max) return s;
        return s[..(max - 1)] + "…";
    }
}
