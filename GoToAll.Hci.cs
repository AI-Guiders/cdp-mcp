using Cdp.Core;
using Cdp.ScriptableIde;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// HCI lane for Go To All — codebase_index_search (SQLite FTS) before the file walk.
/// Covers multi-root sessions and languages the Roslyn walk can't parse (.fs, .md, …).
/// The MCP handler auto-reindexes once on missing/empty index. Falls back to the
/// walk when the index stays unavailable.
/// </summary>
internal static partial class GoToAll
{
    static bool TryHciSearch(List<Hit> hits, SessionContext session, string query, int max)
    {
        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["query"] = JsonSerializer.SerializeToElement(query),
                ["workspace_path"] = JsonSerializer.SerializeToElement(root),
                ["max"] = JsonSerializer.SerializeToElement(Math.Max(max, 15)),
            };

            // Through ToolHandlers — auto-reindexes once on missing/empty index and retries.
            var raw = HybridCodebaseIndex.Mcp.ToolHandlers.Handle("codebase_index_search", args);
            if (string.IsNullOrEmpty(raw))
                return false;

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("hits", out var hitsEl)
                || hitsEl.ValueKind != JsonValueKind.Array)
                return false;

            var added = 0;
            foreach (var h in hitsEl.EnumerateArray())
            {
                if (added >= max)
                    break;
                if (!h.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
                    continue;
                var path = pathEl.GetString() ?? "";
                var rank = h.TryGetProperty("rankScore", out var rankEl)
                           && rankEl.ValueKind == JsonValueKind.Number
                    ? rankEl.GetDouble()
                    : 1.0;
                var line = h.TryGetProperty("lineStart", out var lineEl)
                           && lineEl.ValueKind == JsonValueKind.Number
                    ? lineEl.GetInt32()
                    : 1;

                var name = System.IO.Path.GetFileName(path);
                // Map FTS rank into the walk's score band (below exact-name 1000/800,
                // above fuzzy camel 300) so exact file/type matches still win.
                var score = Math.Clamp((int)Math.Round(rank * 100) + 400, 350, 700);
                var anchor = BracketLocate.Format(new BracketLocate.Span(
                    FileLabel(session, path), null, line, null));
                hits.Add(new Hit("hci_text", name, score, anchor));
                added++;
            }

            return added > 0;
        }
        catch
        {
            // Index disabled, DB locked, workspace not indexed — fall back to the walk.
            return false;
        }
    }
}