using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
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
}
