using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>
    /// stage_events_v2 uses OutWit-native GUID / DATETIMEOFFSET.
    /// Legacy <c>stage_events</c> (TEXT Id/StageId) made EF <c>Where(StageId == guid)</c>
    /// return empty while same-context SaveChanges still looked successful.
    /// New table name avoids DROP races on remount / dual-seat Ensure*.
    /// Do not DROP/rebuild durable rows to "heal" StageId filters — rewrite can corrupt Utc.
    /// Dig/list/review use <see cref="StageEventsForStage"/> (client Guid match) instead.
    /// </summary>
    public void EnsureStageEventsTable()
    {
        WithDb(CreateStageEventsV2Table);
    }

    static void CreateStageEventsV2Table(IntentWorkspaceDbContext db)
    {
        db.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS stage_events_v2 (
                Id GUID NOT NULL PRIMARY KEY,
                StageId GUID NOT NULL,
                Utc DATETIMEOFFSET NOT NULL,
                Kind TEXT NOT NULL,
                Source TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Ref TEXT NULL
            );
            """);
        try
        {
            db.Database.ExecuteSqlRaw(
                "CREATE INDEX IF NOT EXISTS IX_stage_events_v2_StageId_Utc ON stage_events_v2 (StageId, Utc);");
        }
        catch
        {
            // index already exists / engine variance
        }

        HealNullStageEventUtc(db);
    }

    /// <summary>
    /// Client StageId match: OutWit server Where(StageId==guid) can return empty on durable
    /// WitDB while materialize+Guid equality still works (bytes equal).
    /// </summary>
    static List<StageEventEntity> StageEventsForStage(IntentWorkspaceDbContext db, Guid stageId)
    {
        // Fast path: server filter (works when OutWit GUID Where is honest).
        try
        {
            var server = db.StageEvents.Where(e => e.StageId == stageId).ToList();
            if (server.Count > 0)
                return server;
        }
        catch
        {
            HealNullStageEventUtc(db);
        }

        // Client Guid match — needed when OutWit Where returns empty on durable WitDB.
        // Never let NULL Utc / materialize faults kill callers silently empty.
        try
        {
            return db.StageEvents.AsEnumerable().Where(e => e.StageId == stageId).ToList();
        }
        catch
        {
            HealNullStageEventUtc(db);
            try
            {
                return db.StageEvents.AsEnumerable().Where(e => e.StageId == stageId).ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>DROP-heal of StageId filters can leave Utc NULL — EF then throws on any materialize.</summary>
    static void HealNullStageEventUtc(IntentWorkspaceDbContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw("DELETE FROM stage_events_v2 WHERE Utc IS NULL;");
        }
        catch
        {
            /* table / engine variance */
        }
    }


    /// <summary>
    /// Append only while wall clock open (Started set, Completed null).
    /// Returns false when clock closed — caller should not treat as error.
    /// </summary>
    public bool StageEventTryAppendOpenClock(
        Guid stageId, string kind, string source, string summary, string? refId = null)
    {
        var k = (kind ?? "").Trim();
        var src = (source ?? "").Trim();
        if (k.Length == 0 || src.Length == 0)
            return false;
        var sum = TruncateSummary(summary);
        var now = DateTimeOffset.UtcNow;
        return WithDb(db =>
        {
            var stage = db.Stages.FirstOrDefault(x => x.Id == stageId);
            if (stage is null || stage.StartedUtc is null || stage.CompletedUtc is not null)
                return false;
            var id = Guid.NewGuid();
            db.StageEvents.Add(new StageEventEntity
            {
                Id = id,
                StageId = stageId,
                Utc = now,
                Kind = k.Length > 64 ? k[..64] : k,
                Source = src.Length > 32 ? src[..32] : src,
                Summary = sum,
                Ref = string.IsNullOrWhiteSpace(refId) ? null : TruncateSummary(refId, 80)
            });
            if (db.SaveChanges() <= 0)
                return false;
            db.ChangeTracker.Clear();
            return db.StageEvents.Any(e => e.Id == id);
        });
    }

    public object StageEventNote(IntentWorkspaceState state, Guid stageId, string text)
    {
        var intentId = RequireIntent(state);
        var note = TruncateSummary(text);
        if (note.Length == 0)
            throw new ArgumentException("note needs text — note <text>");
        return WithDb(db =>
        {
            var stage = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                        ?? throw new ArgumentException($"stage_id not found: {stageId}");
            if (stage.StartedUtc is null || stage.CompletedUtc is not null)
                throw new ArgumentException("note needs open clock — cmd=start first");
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            db.StageEvents.Add(new StageEventEntity
            {
                Id = id,
                StageId = stageId,
                Utc = now,
                Kind = "note",
                Source = "agent",
                Summary = note,
                Ref = null
            });
            if (db.SaveChanges() <= 0)
                throw new InvalidOperationException("stage_events note SaveChanges wrote 0 rows");
            db.ChangeTracker.Clear();
            if (!db.StageEvents.Any(e => e.Id == id))
                throw new InvalidOperationException(
                    "stage_events note not durable after save — WitDB flush/schema failure");
            return new
            {
                op = "note",
                task_id = stageId,
                kind = "note",
                utc = now,
                summary = note,
                hint = "append-only pointer — SA diagnostic, not a score"
            };
        });
    }

    public object StageEventList(IntentWorkspaceState state, Guid stageId, int take = 40)
    {
        var intentId = RequireIntent(state);
        take = Math.Clamp(take, 1, 100);
        return WithDb(db =>
        {
            _ = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var rows = StageEventsForStage(db, stageId)
                .OrderBy(e => e.Utc)
                .Take(take)
                .Select(e => new
                {
                    utc = e.Utc,
                    kind = e.Kind,
                    source = e.Source,
                    summary = e.Summary,
                    @ref = e.Ref
                })
                .ToList();
            var counts = CountKinds(rows.Select(r => r.kind));
            return new
            {
                op = "events",
                task_id = stageId,
                count = rows.Count,
                wait = counts.Wait,
                fail = counts.Fail,
                note = counts.Note,
                events = rows,
                hint = "append-only pointers bound to open wall clock — not a score"
            };
        });
    }

    public (int Wait, int Fail, int Note) StageEventCounts(Guid stageId) =>
        WithDb(db => CountKinds(StageEventsForStage(db, stageId).Select(e => e.Kind)));

    /// <summary>phase.start / phase.complete rows for wall segment formatting.</summary>
    public IReadOnlyList<(string Kind, string Summary, DateTimeOffset Utc)> StageEventPhaseRows(Guid stageId) =>
        WithDb(db =>
            StageEventsForStage(db, stageId)
                .Where(e => e.Kind == "phase.start" || e.Kind == "phase.complete")
                .OrderBy(e => e.Utc)
                .Select(e => (e.Kind, e.Summary, e.Utc))
                .ToList());

    static (int Wait, int Fail, int Note) CountKinds(IEnumerable<string> kinds)
    {
        var wait = 0;
        var fail = 0;
        var note = 0;
        foreach (var k in kinds)
        {
            if (k.Equals("note", StringComparison.OrdinalIgnoreCase))
                note++;
            else if (k.Contains("wait", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("busy", StringComparison.OrdinalIgnoreCase))
                wait++;
            else if (k.Contains("fail", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("chat_not_found", StringComparison.OrdinalIgnoreCase))
                fail++;
        }

        return (wait, fail, note);
    }

    static string TruncateSummary(string? s, int max = 120)
    {
        var t = (s ?? "").Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (t.Length <= max)
            return t;
        return t[..(max - 1)] + "…";
    }
}
