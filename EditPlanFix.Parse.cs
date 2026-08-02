using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;
internal static partial class EditPlanFix
{
    public sealed record DiagHit(string Id, string File, int Line, int Column, int EndLine, int EndColumn, string? Message);
    sealed record ActionItem(int Index, string Title);
    static ActionItem? PickAction(IReadOnlyList<ActionItem> actions, string diagnosticId)
    {
        // Prefer title that mentions the id.
        var byId = actions.FirstOrDefault(a => a.Title.Contains(diagnosticId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
            return byId;
        // Known title heuristics (stable ids remembered by humans as names).
        if (diagnosticId.Equals("IDE0005", StringComparison.OrdinalIgnoreCase) || diagnosticId.Equals("CS8019", StringComparison.OrdinalIgnoreCase))
        {
            var u = actions.FirstOrDefault(a => a.Title.Contains("Remove Unnecessary Using", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("Remove unused using", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("unnecessary using", StringComparison.OrdinalIgnoreCase));
            if (u is not null)
                return u;
        }

        if (diagnosticId.Equals("CS0219", StringComparison.OrdinalIgnoreCase))
        {
            var u = actions.FirstOrDefault(a => a.Title.Contains("Remove unused", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("unused variable", StringComparison.OrdinalIgnoreCase));
            if (u is not null)
                return u;
        }

        // Prefer Fix-looking titles over pure refactorings when multiple.
        var fixish = actions.LastOrDefault(a => a.Title.Contains("Remove", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("Fix", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("Simplify", StringComparison.OrdinalIgnoreCase) || a.Title.Contains("Use ", StringComparison.OrdinalIgnoreCase));
        return fixish ?? actions.LastOrDefault();
    }

    static List<ActionItem> ParseActionList(string raw)
    {
        var list = new List<ActionItem>();
        foreach (var line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = ActionLine.Match(line);
            if (!m.Success)
                continue;
            list.Add(new ActionItem(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
        }

        return list;
    }

    static bool TryParseDiagItems(string raw, out List<DiagHit> items, out string? error)
    {
        items = [];
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                error = root.TryGetProperty("error", out var e) ? e.GetString() : "diagnostics failed";
                return false;
            }

            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                // Some envelopes nest differently — try items at root.
                if (!root.TryGetProperty("items", out arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    error = "diagnostics payload missing data.items";
                    return false;
                }
            }

            foreach (var el in arr.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (id.Length == 0)
                    continue;
                var file = el.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "";
                var line = el.TryGetProperty("line", out var l) && l.TryGetInt32(out var li) ? li : 1;
                var col = el.TryGetProperty("column", out var c) && c.TryGetInt32(out var ci) ? ci : 1;
                var endLine = el.TryGetProperty("end_line", out var eln) && eln.TryGetInt32(out var eli) ? eli : line;
                var endCol = el.TryGetProperty("end_column", out var ec) && ec.TryGetInt32(out var eci) ? eci : col + 1;
                var msg = el.TryGetProperty("message", out var m) ? m.GetString() : null;
                items.Add(new DiagHit(id, file, line, col, endLine, endCol, msg));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"diagnostics parse: {ex.Message}";
            return false;
        }
    }

    static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    static string YamlQuote(string s) => s.Contains(':') || s.Contains('#') || s.Contains('\'') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : s;
    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    [GeneratedRegex(@"^(\d+)\t(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ActionLineRegex();
}