using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    public object ScenePark(
        IntentWorkspaceState state,
        SessionContext session,
        string name,
        string? loot,
        string? focusPath,
        int? focusLine,
        Guid? bindStageId)
    {
        lock (DbGate)
        {
            var intentId = RequireIntent(state);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required for scene_park.");
            name = name.Trim();
            using var db = Open();
            var now = DateTimeOffset.UtcNow;
            var entity = db.Scenes.FirstOrDefault(x => x.IntentId == intentId && x.Name == name);
            if (entity is null)
            {
                entity = new SceneEntity
                {
                    Id = Guid.NewGuid(),
                    IntentId = intentId,
                    Name = name
                };
                db.Scenes.Add(entity);
            }

            entity.SnapshotJson = SessionSnapshot.Capture(session);
            if (loot is not null)
                entity.Loot = loot;
            if (focusPath is not null)
                entity.FocusPath = string.IsNullOrWhiteSpace(focusPath) ? null : focusPath;
            if (focusLine is not null)
                entity.FocusLine = focusLine >= 1 ? focusLine : null;
            entity.UpdatedUtc = now;

            if (bindStageId is { } sid)
            {
                var stage = db.Stages.FirstOrDefault(x => x.Id == sid && x.IntentId == intentId)
                            ?? throw new ArgumentException($"bind_stage_id not found: {sid}");
                stage.SceneId = entity.Id;
                stage.UpdatedUtc = now;
            }

            db.SaveChanges();
            state.ActiveSceneId = entity.Id;
            return SceneDto(entity);
        }
    }

    public object SceneSwitch(
        IntentWorkspaceState state,
        SessionContext session,
        string name,
        Action notifyListChanged)
    {
        lock (DbGate)
        {
            var intentId = RequireIntent(state);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required for scene_switch.");
            name = name.Trim();
            using var db = Open();
            var now = DateTimeOffset.UtcNow;

            if (state.ActiveSceneId is { } currentId)
            {
                var current = db.Scenes.FirstOrDefault(x => x.Id == currentId && x.IntentId == intentId);
                if (current is not null)
                {
                    current.SnapshotJson = SessionSnapshot.Capture(session);
                    current.UpdatedUtc = now;
                }
            }
            else
            {
                var autosave = db.Scenes.FirstOrDefault(x => x.IntentId == intentId && x.Name == "_autosave");
                if (autosave is null)
                {
                    autosave = new SceneEntity
                    {
                        Id = Guid.NewGuid(),
                        IntentId = intentId,
                        Name = "_autosave"
                    };
                    db.Scenes.Add(autosave);
                }

                autosave.SnapshotJson = SessionSnapshot.Capture(session);
                autosave.UpdatedUtc = now;
            }

            var target = db.Scenes.FirstOrDefault(x => x.IntentId == intentId && x.Name == name)
                         ?? throw new ArgumentException($"scene not found: {name}");
            SessionSnapshot.Apply(session, target.SnapshotJson);
            state.ActiveSceneId = target.Id;
            db.SaveChanges();
            notifyListChanged();

            var openStages = db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId && (x.Status == "active" || x.Status == "pending"))
                .OrderBy(x => x.Ordinal)
                .Select(x => new { stage_id = x.Id, title = x.Title, status = x.Status })
                .ToList();

            return new
            {
                switched_to = target.Name,
                scene_id = target.Id,
                loot = target.Loot,
                focus_path = target.FocusPath,
                focus_line = target.FocusLine,
                session = JsonSerializer.Deserialize<JsonElement>(session.ToJson()),
                stages_open = openStages
            };
        }
    }

    public object SceneList(IntentWorkspaceState state)
    {
        var intentId = RequireIntent(state);
        using var db = Open();
        var rows = db.Scenes.AsNoTracking()
            .Where(x => x.IntentId == intentId)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                scene_id = x.Id,
                name = x.Name,
                focus_path = x.FocusPath,
                focus_line = x.FocusLine,
                loot = x.Loot,
                updated_utc = x.UpdatedUtc
            })
            .ToList();
        return new { intent_id = intentId, scenes = rows };
    }

    public object Status(IntentWorkspaceState state, SessionContext session)
    {
        using var db = Open();
        string? intentTitle = null;
        string? sceneName = null;
        string? stageTitle = null;
        if (state.ActiveIntentId is { } iid)
            intentTitle = db.Intents.AsNoTracking().Where(x => x.Id == iid).Select(x => x.Title).FirstOrDefault();
        if (state.ActiveSceneId is { } sid)
            sceneName = db.Scenes.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).FirstOrDefault();
        if (state.ActiveStageId is { } stid)
            stageTitle = db.Stages.AsNoTracking().Where(x => x.Id == stid).Select(x => x.Title).FirstOrDefault();

        return new
        {
            database_path = state.DatabasePath,
            active_intent_id = state.ActiveIntentId,
            active_intent_title = intentTitle,
            active_stage_id = state.ActiveStageId,
            active_stage_title = stageTitle,
            active_scene_id = state.ActiveSceneId,
            active_scene_name = sceneName,
            session = JsonSerializer.Deserialize<JsonElement>(session.ToJson())
        };
    }

    public (string? IntentId, string? SceneId, string? SceneName, string DatabasePath) PlaneIds(IntentWorkspaceState state)
    {
        using var db = Open();
        string? sceneName = null;
        if (state.ActiveSceneId is { } sid)
            sceneName = db.Scenes.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).FirstOrDefault();

        return (
            state.ActiveIntentId?.ToString("D"),
            state.ActiveSceneId?.ToString("D"),
            sceneName,
            state.DatabasePath);
    }

    private static Guid RequireIntent(IntentWorkspaceState state) =>
        state.ActiveIntentId ?? throw new ArgumentException("No active intent. Call op=intent_upsert or intent_select first.");

    private static object StageJobDto(StageEntity e) => new
    {
        stage_id = e.Id,
        intent_id = e.IntentId,
        title = e.Title,
        status = e.Status,
        scene_id = e.SceneId,
        ordinal = e.Ordinal,
        loot = e.Loot,
        job_json = e.JobJson,
        job_error = e.JobError,
        updated_utc = e.UpdatedUtc
    };

    private static object SceneDto(SceneEntity e) => new
    {
        scene_id = e.Id,
        name = e.Name,
        focus_path = e.FocusPath,
        focus_line = e.FocusLine,
        loot = e.Loot,
        updated_utc = e.UpdatedUtc
    };

    public const int OpenRecentCapacity = 20;

    /// <summary>Existing WitDB may predate open_recent — EnsureCreated won't alter; CREATE IF NOT EXISTS.</summary>
}
