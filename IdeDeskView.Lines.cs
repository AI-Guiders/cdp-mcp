#nullable enable
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeDeskView
{
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

}
