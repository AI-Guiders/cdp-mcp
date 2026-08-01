using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public IntentUpsertResult IntentUpsert(IntentWorkspaceState state, string title, Guid? intentId)
    {
        var now = DateTimeOffset.UtcNow;
        var result = WithDb(db =>
        {
            IntentEntity entity;
            if (intentId is { } id)
            {
                entity = db.Intents.FirstOrDefault(x => x.Id == id)
                         ?? throw new ArgumentException($"intent_id not found: {id}");
                if (!string.IsNullOrWhiteSpace(title))
                    entity.Title = title.Trim();
                entity.UpdatedUtc = now;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("title is required for intent_upsert.");
                entity = new IntentEntity
                {
                    Id = Guid.NewGuid(),
                    Title = title.Trim(),
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                db.Intents.Add(entity);
            }

            db.SaveChanges();
            state.ActiveIntentId = entity.Id;
            return new IntentUpsertResult(entity.Id, entity.Title, active: true);
        });
        WorkFocusSave(state);
        return result;
    }

    public object IntentList() =>
        WithDb(db =>
        {
            var rows = db.Intents.AsNoTracking()
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => new { intent_id = x.Id, title = x.Title, updated_utc = x.UpdatedUtc })
                .ToList();
            return (object)new { intents = rows };
        });

    public IntentUpsertResult IntentSelect(IntentWorkspaceState state, Guid intentId)
    {
        var result = WithDb(db =>
        {
            var entity = db.Intents.AsNoTracking().FirstOrDefault(x => x.Id == intentId)
                         ?? throw new ArgumentException($"intent_id not found: {intentId}");
            state.ActiveIntentId = entity.Id;
            state.ActiveSceneId = null;
            state.ActiveStageId = null;
            return new IntentUpsertResult(entity.Id, entity.Title, active: true);
        });
        WorkFocusSave(state);
        return result;
    }

    public object IntentDelete(IntentWorkspaceState state, Guid intentId)
    {
        string title = "";
        Guid? nextIntent = null;
        WithDb(db =>
        {
            var entity = db.Intents.FirstOrDefault(x => x.Id == intentId)
                         ?? throw new ArgumentException($"intent_id not found: {intentId}");
            title = entity.Title;
            db.Stages.RemoveRange(db.Stages.Where(x => x.IntentId == intentId));
            db.Scenes.RemoveRange(db.Scenes.Where(x => x.IntentId == intentId));
            db.Intents.Remove(entity);
            db.SaveChanges();
            nextIntent = db.Intents.AsNoTracking()
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault();
        });

        if (state.ActiveIntentId == intentId)
        {
            state.ActiveIntentId = nextIntent;
            state.ActiveStageId = null;
            state.ActiveSceneId = null;
            WorkFocusSave(state);
        }

        return new { op = "feature_drop", feature_id = intentId, title };
    }

    public object StageDelete(IntentWorkspaceState state, Guid stageId)
    {
        var intentId = RequireIntent(state);
        string title = "";
        var clearFocus = false;
        WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            title = entity.Title;
            DeleteStageTreeUnlocked(db, stageId);
            db.SaveChanges();
            clearFocus = state.ActiveStageId == stageId
                         || (state.ActiveStageId is { } aid
                             && !db.Stages.AsNoTracking().Any(x => x.Id == aid));
        });

        if (clearFocus)
        {
            state.ActiveStageId = null;
            WorkFocusSave(state);
        }

        return new { op = "task_drop", task_id = stageId, title };
    }

    static void DeleteStageTreeUnlocked(IntentWorkspaceDbContext db, Guid stageId)
    {
        var children = db.Stages.Where(x => x.ParentId == stageId).Select(x => x.Id).ToList();
        foreach (var child in children)
            DeleteStageTreeUnlocked(db, child);
        db.StageCriteria.RemoveRange(db.StageCriteria.Where(x => x.StageId == stageId));
        db.StageEvents.RemoveRange(db.StageEvents.Where(x => x.StageId == stageId));
        var row = db.Stages.FirstOrDefault(x => x.Id == stageId);
        if (row is not null)
            db.Stages.Remove(row);
    }

}
