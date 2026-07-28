using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Existing WitDB may predate stage_events — CREATE IF NOT EXISTS.</summary>
    public void EnsureStageEventsTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS stage_events (
                    Id TEXT NOT NULL PRIMARY KEY,
                    StageId TEXT NOT NULL,
                    Utc TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    Summary TEXT NOT NULL,
                    Ref TEXT NULL
                );
                """);
            try
            {
                db.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS IX_stage_events_StageId_Utc ON stage_events (StageId, Utc);");
            }
            catch
            {
                // index already exists / engine variance
            }
        });
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
            db.StageEvents.Add(new StageEventEntity
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                Utc = now,
                Kind = k.Length > 64 ? k[..64] : k,
                Source = src.Length > 32 ? src[..32] : src,
                Summary = sum,
                Ref = string.IsNullOrWhiteSpace(refId) ? null : TruncateSummary(refId, 80)
            });
            db.SaveChanges();
            return true;
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
                throw new ArgumentException("note needs open clock — cmd=start first (not yet shipped)");
            var now = DateTimeOffset.UtcNow;
            var row = new StageEventEntity
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                Utc = now,
                Kind = "note",
                Source = "agent",
                Summary = note,
                Ref = null
            };
            db.StageEvents.Add(row);
            db.SaveChanges();
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
            var rows = db.StageEvents
                .Where(e => e.StageId == stageId)
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
        WithDb(db =>
        {
            var kinds = db.StageEvents.Where(e => e.StageId == stageId).Select(e => e.Kind).ToList();
            return CountKinds(kinds);
        });

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
