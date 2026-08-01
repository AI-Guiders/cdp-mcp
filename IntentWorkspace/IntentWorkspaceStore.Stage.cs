using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public StageUpsertResult StageUpsert(
        IntentWorkspaceState state,
        string title,
        Guid? stageId,
        Guid? parentId,
        string? sceneName,
        string? phaseAffinity = null)
    {
        var intentId = RequireIntent(state);
        var now = DateTimeOffset.UtcNow;
        string? affinity = NormalizePhaseAffinity(phaseAffinity);
        return WithDb(db =>
        {
            Guid? sceneId = null;
            if (!string.IsNullOrWhiteSpace(sceneName))
            {
                var scene = db.Scenes.FirstOrDefault(x => x.IntentId == intentId && x.Name == sceneName.Trim())
                            ?? throw new ArgumentException($"scene not found: {sceneName}");
                sceneId = scene.Id;
            }

            StageEntity entity;
            if (stageId is { } id)
            {
                entity = db.Stages.FirstOrDefault(x => x.Id == id && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {id}");
                if (!string.IsNullOrWhiteSpace(title))
                    entity.Title = title.Trim();
                if (parentId.HasValue)
                    entity.ParentId = parentId;
                if (sceneId.HasValue)
                    entity.SceneId = sceneId;
                if (phaseAffinity is not null)
                    entity.PhaseAffinity = affinity;
                entity.UpdatedUtc = now;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("title is required for stage_upsert.");
                var ordinal = db.Stages.Count(x => x.IntentId == intentId);
                entity = new StageEntity
                {
                    Id = Guid.NewGuid(),
                    IntentId = intentId,
                    ParentId = parentId,
                    Title = title.Trim(),
                    Status = "pending",
                    SceneId = sceneId,
                    Ordinal = ordinal,
                    PhaseAffinity = affinity,
                    UpdatedUtc = now
                };
                db.Stages.Add(entity);
            }

            db.SaveChanges();
            return new StageUpsertResult(
                stage_id: entity.Id,
                title: entity.Title,
                status: entity.Status,
                parent_id: entity.ParentId,
                scene_id: entity.SceneId,
                ordinal: entity.Ordinal,
                phase_affinity: entity.PhaseAffinity);
        });
    }

    static string? NormalizePhaseAffinity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return Cdp.Core.CdpEnumParse.TryParsePhase(raw, out var p)
            ? Cdp.Core.CdpEnumParse.ToWire(p)
            : throw new ArgumentException($"phase affinity must be recall|explore|clarify|plan|act|verify|handoff — got '{raw}'");
    }

    public string? TryGetStagePhaseAffinity(Guid stageId)
    {
        return WithDb(db =>
            db.Stages.AsNoTracking()
                .Where(x => x.Id == stageId)
                .Select(x => x.PhaseAffinity)
                .FirstOrDefault());
    }

    public object StageList(IntentWorkspaceState state)
    {
        var intentId = RequireIntent(state);
        return WithDb(db =>
        {
            var rows = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId)
                .OrderBy(x => x.Ordinal)
                .ToList();
            var stageIds = rows.Select(x => x.Id).ToList();
            var criteria = db.StageCriteria.AsNoTracking()
                .Where(x => stageIds.Contains(x.StageId))
                .ToList();
            var byStage = criteria.GroupBy(x => x.StageId)
                .ToDictionary(g => g.Key, g => g.ToList());
            var stages = rows.Select(x =>
            {
                byStage.TryGetValue(x.Id, out var list);
                list ??= [];
                return new
                {
                    stage_id = x.Id,
                    parent_id = x.ParentId,
                    title = x.Title,
                    status = x.Status,
                    scene_id = x.SceneId,
                    ordinal = x.Ordinal,
                    phase_affinity = x.PhaseAffinity,
                    has_loot = x.Loot != null,
                    has_job = x.JobJson != null,
                    job_error = x.JobError,
                    criteria = BuildCriteriaSummary(list)
                };
            }).ToList();
            return (object)new { intent_id = intentId, stages };
        });
    }

    public StageSetStatusResult StageSetStatus(IntentWorkspaceState state, Guid stageId, string status)
    {
        if (!StageStatuses.Contains(status))
            throw new ArgumentException("status must be pending|active|done|parked|deferred.");
        var intentId = RequireIntent(state);
        return WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.Status = status.Trim().ToLowerInvariant();
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
            return new StageSetStatusResult(entity.Id, entity.Status);
        });
    }

    /// <summary>Create active stage with job payload for background IdeReport.</summary>
    public object StageEnqueue(
        IntentWorkspaceState state,
        string title,
        string jobJson,
        Guid? sceneId = null)
    {
        lock (DbGate)
        {
            var intentId = RequireIntent(state);
            if (string.IsNullOrWhiteSpace(jobJson))
                throw new ArgumentException("job_json is required for stage_enqueue.");
            using var db = Open();
            var now = DateTimeOffset.UtcNow;
            var ordinal = db.Stages.Count(x => x.IntentId == intentId);
            var entity = new StageEntity
            {
                Id = Guid.NewGuid(),
                IntentId = intentId,
                Title = string.IsNullOrWhiteSpace(title) ? "ide-report" : title.Trim(),
                Status = "active",
                SceneId = sceneId ?? state.ActiveSceneId,
                Ordinal = ordinal,
                JobJson = jobJson,
                UpdatedUtc = now
            };
            db.Stages.Add(entity);
            db.SaveChanges();
            return new
            {
                stage_id = entity.Id,
                title = entity.Title,
                status = entity.Status,
                job_json = entity.JobJson
            };
        }
    }

    public object StageGet(Guid stageId)
    {
        lock (DbGate)
        {
            using var db = Open();
            var entity = db.Stages.AsNoTracking().FirstOrDefault(x => x.Id == stageId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            var criteria = db.StageCriteria.AsNoTracking()
                .Where(x => x.StageId == stageId)
                .OrderBy(x => x.Ordinal)
                .ToList();
            return new
            {
                stage_id = entity.Id,
                intent_id = entity.IntentId,
                title = entity.Title,
                status = entity.Status,
                scene_id = entity.SceneId,
                ordinal = entity.Ordinal,
                loot = entity.Loot,
                job_json = entity.JobJson,
                job_error = entity.JobError,
                phase_affinity = entity.PhaseAffinity,
                updated_utc = entity.UpdatedUtc,
                criteria_summary = BuildCriteriaSummary(criteria),
                criteria = criteria.Select(c => new
                {
                    criterion_id = c.Id,
                    kind = c.Kind,
                    text = c.Body,
                    mode = c.Mode,
                    status = c.Status,
                    evidence_ref = c.EvidenceRef,
                    ordinal = c.Ordinal,
                    updated_utc = c.UpdatedUtc
                }).ToList()
            };
        }
    }

    public void StageCompleteJob(Guid stageId, string lootJson)
    {
        lock (DbGate)
        {
            using var db = Open();
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.Status = "done";
            entity.Loot = lootJson;
            entity.JobError = null;
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
        }
    }

    public void StageFailJob(Guid stageId, string error)
    {
        lock (DbGate)
        {
            using var db = Open();
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            entity.Status = "parked";
            entity.JobError = error;
            entity.Loot = JsonSerializer.Serialize(new
            {
                kind = "error",
                available = false,
                reason = error
            });
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
        }
    }

}
