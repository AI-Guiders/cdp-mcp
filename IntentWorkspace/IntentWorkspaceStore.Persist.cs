using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
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

    /// <summary>Existing WitDB may predate desk_seats — EnsureCreated won't alter; CREATE IF NOT EXISTS.</summary>
    public void EnsureDeskSeatsTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS desk_seats (
                    Seat TEXT NOT NULL PRIMARY KEY,
                    Organ TEXT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """);
        });
    }

    /// <summary>Existing WitDB may predate Stage.PhaseAffinity — ALTER if missing.</summary>
    public void EnsureStagePhaseAffinityColumn()
    {
        WithDb(db =>
        {
            try
            {
                db.Database.ExecuteSqlRaw(
                    "ALTER TABLE stages ADD COLUMN PhaseAffinity TEXT NULL;");
            }
            catch
            {
                // column already exists
            }
        });
    }

    /// <summary>Existing WitDB may predate stage wall clock — ALTER if missing.</summary>
    public void EnsureStageClockColumns()
    {
        WithDb(db =>
        {
            foreach (var sql in new[]
                     {
                         "ALTER TABLE stages ADD COLUMN StartedUtc TEXT NULL;",
                         "ALTER TABLE stages ADD COLUMN CompletedUtc TEXT NULL;"
                     })
            {
                try { db.Database.ExecuteSqlRaw(sql); }
                catch { /* column already exists */ }
            }
        });
    }

    /// <summary>Explicit Start — measurable ship cycle. Does not auto-fire on focus/edit.</summary>
    public object StageClockStart(IntentWorkspaceState state, Guid stageId)
    {
        var intentId = RequireIntent(state);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.StartedUtc = now;
            entity.CompletedUtc = null; // restart cycle
            entity.UpdatedUtc = now;
            db.SaveChanges();
            return new
            {
                op = "start",
                task_id = entity.Id,
                started_utc = entity.StartedUtc,
                completed_utc = (DateTimeOffset?)null,
                elapsed = (string?)null,
                kind = "wall",
                hint = "wall calendar clock — not agent-active; Start is explicit only"
            };
        });
    }

    /// <summary>Explicit Completed after ship — wall end. Elapsed = Completed−Start (calendar).</summary>
    public object StageClockShipped(IntentWorkspaceState state, Guid stageId)
    {
        var intentId = RequireIntent(state);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            if (entity.StartedUtc is null)
                throw new ArgumentException("shipped needs Start first — cmd=start (explicit cycle)");
            entity.CompletedUtc = now;
            entity.UpdatedUtc = now;
            db.SaveChanges();
            var elapsed = IdeTaskManager.FormatWallElapsed(entity.StartedUtc.Value, entity.CompletedUtc.Value);
            var kinds = db.StageEvents.Where(e => e.StageId == entity.Id).Select(e => e.Kind).ToList();
            var counts = CountKinds(kinds);
            var events = IdeTaskManager.FormatEventCountsSuffix(counts.Wait, counts.Fail, counts.Note);
            var phaseRows = db.StageEvents
                .Where(e => e.StageId == entity.Id
                            && (e.Kind == "phase.start" || e.Kind == "phase.complete"))
                .OrderBy(e => e.Utc)
                .Select(e => new ValueTuple<string, string, DateTimeOffset>(e.Kind, e.Summary, e.Utc))
                .ToList();
            var phases = IdeTaskManager.FormatPhaseSegmentsSuffix(phaseRows, entity.CompletedUtc.Value);
            var suffix = phases + events;
            return new
            {
                op = "shipped",
                task_id = entity.Id,
                started_utc = entity.StartedUtc,
                completed_utc = entity.CompletedUtc,
                elapsed,
                wait = counts.Wait,
                fail = counts.Fail,
                note = counts.Note,
                events_suffix = suffix.Length == 0 ? null : suffix.TrimStart(' ', '·').Trim(),
                kind = "wall",
                hint = "SA tempo (wall) — not a score; events=pointers not reward"
            };
        });
    }

    /// <summary>Park freezes an open wall clock — calendar stop without claiming ship. Resume via explicit start (restarts cycle).</summary>
    public object? StageClockParkFreeze(IntentWorkspaceState state, Guid stageId)
    {
        var intentId = RequireIntent(state);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            if (entity.StartedUtc is null || entity.CompletedUtc is not null)
                return null;
            entity.CompletedUtc = now;
            entity.UpdatedUtc = now;
            db.SaveChanges();
            var elapsed = IdeTaskManager.FormatWallElapsed(entity.StartedUtc.Value, entity.CompletedUtc.Value);
            return new
            {
                op = "park_freeze",
                task_id = entity.Id,
                started_utc = entity.StartedUtc,
                completed_utc = entity.CompletedUtc,
                elapsed,
                kind = "wall",
                hint = "park froze open wall — not shipped; start again to resume measurable cycle"
            };
        });
    }

    public void DeskSeatsSave(IReadOnlyDictionary<string, string?> seats)
    {
        var now = DateTimeOffset.UtcNow;
        WithDb(db =>
        {
            foreach (var (seat, organ) in seats)
            {
                var row = db.DeskSeats.Find(seat);
                if (row is null)
                {
                    db.DeskSeats.Add(new DeskSeatEntity
                    {
                        Seat = seat,
                        Organ = string.IsNullOrWhiteSpace(organ) ? null : organ.Trim(),
                        UpdatedUtc = now
                    });
                }
                else
                {
                    row.Organ = string.IsNullOrWhiteSpace(organ) ? null : organ.Trim();
                    row.UpdatedUtc = now;
                }
            }

            db.SaveChanges();
        });
    }

    /// <returns>false if table empty / never saved.</returns>
    public bool DeskSeatsTryLoad(IDictionary<string, string?> into)
    {
        return WithDb(db =>
        {
            var rows = db.DeskSeats.AsNoTracking().ToList();
            if (rows.Count == 0)
                return false;
            foreach (var key in into.Keys.ToList())
                into[key] = null;
            foreach (var row in rows)
            {
                if (into.ContainsKey(row.Seat))
                    into[row.Seat] = string.IsNullOrWhiteSpace(row.Organ) ? null : row.Organ.Trim();
            }

            return true;
        });
    }

    /// <summary>One-shot import from mistaken desk-seats.json then delete.</summary>
}
