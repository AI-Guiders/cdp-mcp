#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public Guid? FindIntentIdByTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var t = title.Trim();
        var bare = StripBoardChrome(t);
        return WithDb(db =>
        {
            // Materialize — parity with FindStageMatching (chrome + unique prefix).
            var list = db.Intents.AsNoTracking().ToList();

            Guid? Pick(Func<IntentEntity, bool> pred) =>
                list.Where(pred)
                    .OrderByDescending(x => x.UpdatedUtc)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefault();

            var hit = Pick(x => x.Title == t)
                ?? Pick(x => string.Equals(x.Title, t, StringComparison.OrdinalIgnoreCase))
                ?? (bare.Length > 0 && bare != t
                    ? Pick(x => x.Title == bare)
                        ?? Pick(x => string.Equals(x.Title, bare, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? Pick(x => string.Equals(StripBoardChrome(x.Title), bare, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;

            if (bare.Length < 8)
                return null;
            var prefix = list
                .Where(x =>
                {
                    var stored = StripBoardChrome(x.Title);
                    return stored.StartsWith(bare, StringComparison.OrdinalIgnoreCase)
                           || x.Title.StartsWith(t, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(x => x.UpdatedUtc)
                .ToList();
            return prefix.Count == 1 ? prefix[0].Id : null;
        });
    }


    internal static string StripBoardChrome(string title)
    {
        // Peel trailing board chrome (@act/@todo/#CDP) so feature/task paste matches stored title.
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        while (words.Count > 0)
        {
            var last = words[^1];
            if ((last.StartsWith('@') || last.StartsWith('#')) && last.Length > 1)
            {
                words.RemoveAt(words.Count - 1);
                continue;
            }

            break;
        }

        return string.Join(' ', words);
    }

}
