#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>CDT <c>/json/list</c> page candidate for AutoIgnition.</summary>
    internal readonly record struct IgnitePageTarget(string Title, string WsUrl, int Score);

    /// <summary>
    /// Editor tabs (md/cs/…) expose editable surfaces and used to win ConnectPage (first page wins).
    /// Prefer Agent shell; skip file editors unless nothing else exists.
    /// </summary>
    static readonly Regex EditorTabExtRx = new(
        @"\.(md|markdown|cs|csx|ts|tsx|js|jsx|json|toml|py|rs|go|txt|xml|yml|yaml|html|css|sql|ps1|sh)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Public for tests — rank/filter CDT page targets.</summary>
    internal static IReadOnlyList<IgnitePageTarget> RankPageTargets(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
            return [];

        var all = new List<IgnitePageTarget>();
        foreach (var el in list.EnumerateArray())
        {
            if (!el.TryGetProperty("type", out var t) || t.GetString() is not "page")
                continue;
            if (!el.TryGetProperty("webSocketDebuggerUrl", out var wsEl))
                continue;
            var ws = wsEl.GetString();
            if (string.IsNullOrWhiteSpace(ws))
                continue;

            var title = el.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
            var url = el.TryGetProperty("url", out var uu) ? uu.GetString() ?? "" : "";
            all.Add(new IgnitePageTarget(title, ws, ScoreIgnitePage(title, url)));
        }

        if (all.Count == 0)
            return [];

        // Prefer non-editor pages; only fall back to editors if that is all we have.
        var preferred = all.Where(p => p.Score >= 0).OrderByDescending(p => p.Score).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        if (preferred.Count > 0)
            return preferred;

        return all.OrderByDescending(p => p.Score).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Public for tests.</summary>
    internal static int ScoreIgnitePage(string? title, string? url = null)
    {
        var t = title ?? "";
        var u = url ?? "";

        if (LooksLikeEditorTab(t))
            return -1000;

        var score = 0;
        if (t.Contains("Cursor Agents", StringComparison.OrdinalIgnoreCase))
            score += 1000;
        else if (t.Contains("Agents", StringComparison.OrdinalIgnoreCase)
                 && !t.Contains("Agent Notes", StringComparison.OrdinalIgnoreCase))
            score += 400;

        // Agent panel often still uses the workbench URL; do not punish it.
        if (u.Contains("workbench", StringComparison.OrdinalIgnoreCase))
            score += 5;

        // Empty / generic titles beat file editors (already filtered) but lose to Agents.
        if (string.IsNullOrWhiteSpace(t))
            score -= 20;

        return score;
    }

    /// <summary>Public for tests — md/cs editor titles like <c>foo.md - workspace - Cursor</c>.</summary>
    internal static bool LooksLikeEditorTab(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0)
            return false;

        // Never treat the Agents shell as an editor even if naming drifts.
        if (t.Contains("Cursor Agents", StringComparison.OrdinalIgnoreCase))
            return false;

        if (EditorTabExtRx.IsMatch(t))
            return true;

        // VS Code / Cursor editor chrome: "name — folder (Workspace) — Cursor"
        if (t.Contains("(Workspace)", StringComparison.OrdinalIgnoreCase)
            && t.Contains("Cursor", StringComparison.OrdinalIgnoreCase)
            && t.Contains(" - ", StringComparison.Ordinal))
            return true;

        return false;
    }
}
