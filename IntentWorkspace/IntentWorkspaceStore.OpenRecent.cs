using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public void EnsureOpenRecentTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS open_recent (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Path TEXT NOT NULL,
                    Root TEXT NULL,
                    Kind TEXT NULL,
                    Language TEXT NULL,
                    OpenedUtc TEXT NOT NULL
                );
                """);
        });
    }

    public void OpenRecentPush(string path, string? root, string? kind, string? language)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var full = Path.GetFullPath(path.Trim());
        var rootFull = root is { Length: > 0 }
            ? Path.GetFullPath(root)
            : Path.GetDirectoryName(full);
        var now = DateTimeOffset.UtcNow;
        WithDb(db =>
        {
            var existing = db.OpenRecent.ToList()
                .Where(x => string.Equals(x.Path, full, StringComparison.OrdinalIgnoreCase))
                .ToList();
            db.OpenRecent.RemoveRange(existing);
            db.OpenRecent.Add(new OpenRecentEntity
            {
                Id = Guid.NewGuid(),
                Path = full,
                Root = rootFull,
                Kind = kind,
                Language = language,
                OpenedUtc = now
            });
            db.SaveChanges();
            var ordered = db.OpenRecent.OrderByDescending(x => x.OpenedUtc).ToList();
            if (ordered.Count > OpenRecentCapacity)
            {
                db.OpenRecent.RemoveRange(ordered.Skip(OpenRecentCapacity));
                db.SaveChanges();
            }
        });
    }

    public IReadOnlyList<(string Path, string? Root, string? Kind, string? Language, DateTimeOffset OpenedUtc)> OpenRecentList(
        int take = OpenRecentCapacity)
    {
        return WithDb(db =>
        {
            var rows = db.OpenRecent.AsNoTracking()
                .OrderByDescending(x => x.OpenedUtc)
                .ToList();
            return rows
                .Where(e => File.Exists(e.Path) || Directory.Exists(e.Path))
                .Take(take <= 0 ? OpenRecentCapacity : take)
                .Select(e => (e.Path, e.Root, e.Kind, e.Language, e.OpenedUtc))
                .ToList();
        });
    }

    /// <summary>One-shot import from legacy open-recent.json then delete the file.</summary>
    public void MigrateLegacyOpenRecentJsonIfPresent()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "open-recent.json");
        if (!File.Exists(legacy))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacy));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                File.Delete(legacy);
                return;
            }

            // Import oldest-first so Push order ends with newest on top
            var rows = doc.RootElement.EnumerateArray().Reverse().ToList();
            foreach (var el in rows)
            {
                var path = el.TryGetProperty("path", out var p) ? p.GetString()
                    : el.TryGetProperty("Path", out var p2) ? p2.GetString() : null;
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var root = el.TryGetProperty("root", out var r) ? r.GetString()
                    : el.TryGetProperty("Root", out var r2) ? r2.GetString() : null;
                var kind = el.TryGetProperty("kind", out var k) ? k.GetString()
                    : el.TryGetProperty("Kind", out var k2) ? k2.GetString() : null;
                var lang = el.TryGetProperty("language", out var l) ? l.GetString()
                    : el.TryGetProperty("Language", out var l2) ? l2.GetString() : null;
                OpenRecentPush(path!, root, kind, lang);
            }

            File.Delete(legacy);
        }
        catch
        {
            // leave legacy file if parse failed
        }
    }
}
