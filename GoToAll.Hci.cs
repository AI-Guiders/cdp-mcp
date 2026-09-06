using Cdp.Core;
using Cdp.ScriptableIde;
using HybridCodebaseIndex.Core;

namespace CdpMcp;

/// <summary>
/// HCI lane for Go To All — HybridCodebaseIndex (SQLite FTS) search before the file walk.
/// Covers multi-root sessions and languages the Roslyn walk can't parse (.fs, .md, …).
/// Falls back to the walk when the index is missing/empty/disabled.
/// </summary>
internal static partial class GoToAll
{
    static readonly CodebaseIndexService Hci = new();

    static bool TryHciSearch(List<Hit> hits, SessionContext session, string query, int max)
    {
        var root = session.ProjectRoot;
        if (string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            var (response, error) = Hci
                .SearchAsync(root, query, topN: Math.Max(max, 15))
                .GetAwaiter()
                .GetResult();

            if (error is not null || response.Hits.Count == 0)
                return false;

            foreach (var hit in response.Hits)
            {
                var name = System.IO.Path.GetFileName(hit.Path);
                // Map FTS rank into the walk's score band (below exact-name 1000/800,
                // above fuzzy camel 300) so exact file/type matches still win.
                var score = Math.Clamp((int)Math.Round(hit.RankScore * 100) + 400, 350, 700);
                var anchor = BracketLocate.Format(new BracketLocate.Span(
                    FileLabel(session, hit.Path), null, hit.LineStart, null));
                hits.Add(new Hit("hci_text", name, score, anchor));
            }

            return true;
        }
        catch
        {
            // Index disabled, DB locked, workspace not indexed — fall back to the walk.
            return false;
        }
    }
}