using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed class IntentWorkspaceState
{
    public Guid? ActiveIntentId { get; set; }
    public Guid? ActiveSceneId { get; set; }
    public string DatabasePath { get; init; } = "";
}

internal sealed class IntentWorkspaceStore(DbContextOptions<IntentWorkspaceDbContext> options)
{
    private static readonly Lock DbGate = new();
    private static readonly HashSet<string> StageStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "active", "done", "parked"
    };

    private IntentWorkspaceDbContext Open() => new(options);

    private T WithDb<T>(Func<IntentWorkspaceDbContext, T> action)
    {
        lock (DbGate)
        {
            using var db = Open();
            return action(db);
        }
    }

    private void WithDb(Action<IntentWorkspaceDbContext> action)
    {
        lock (DbGate)
        {
            using var db = Open();
            action(db);
        }
    }

    public IntentUpsertResult IntentUpsert(IntentWorkspaceState state, string title, Guid? intentId)
    {
        using var db = Open();
        var now = DateTimeOffset.UtcNow;
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
    }

    public object IntentList()
    {
        using var db = Open();
        var rows = db.Intents.AsNoTracking()
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new { intent_id = x.Id, title = x.Title, updated_utc = x.UpdatedUtc })
            .ToList();
        return new { intents = rows };
    }

    public IntentUpsertResult IntentSelect(IntentWorkspaceState state, Guid intentId)
    {
        using var db = Open();
        var entity = db.Intents.AsNoTracking().FirstOrDefault(x => x.Id == intentId)
                     ?? throw new ArgumentException($"intent_id not found: {intentId}");
        state.ActiveIntentId = entity.Id;
        state.ActiveSceneId = null;
        return new IntentUpsertResult(entity.Id, entity.Title, active: true);
    }

    public StageUpsertResult StageUpsert(
        IntentWorkspaceState state,
        string title,
        Guid? stageId,
        Guid? parentId,
        string? sceneName)
    {
        var intentId = RequireIntent(state);
        using var db = Open();
        var now = DateTimeOffset.UtcNow;
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
                UpdatedUtc = now
            };
            db.Stages.Add(entity);
        }

        db.SaveChanges();
        return new StageUpsertResult(stage_id: entity.Id, title: entity.Title, status: entity.Status, parent_id: entity.ParentId, scene_id: entity.SceneId, ordinal: entity.Ordinal);
    }

    public object StageList(IntentWorkspaceState state)
    {
        var intentId = RequireIntent(state);
        using var db = Open();
        var rows = db.Stages.AsNoTracking()
            .Where(x => x.IntentId == intentId)
            .OrderBy(x => x.Ordinal)
            .Select(x => new
            {
                stage_id = x.Id,
                parent_id = x.ParentId,
                title = x.Title,
                status = x.Status,
                scene_id = x.SceneId,
                ordinal = x.Ordinal,
                has_loot = x.Loot != null,
                has_job = x.JobJson != null,
                job_error = x.JobError
            })
            .ToList();
        return new { intent_id = intentId, stages = rows };
    }

    public StageSetStatusResult StageSetStatus(IntentWorkspaceState state, Guid stageId, string status)
    {
        if (!StageStatuses.Contains(status))
            throw new ArgumentException("status must be pending|active|done|parked.");
        var intentId = RequireIntent(state);
        using var db = Open();
        var entity = db.Stages.FirstOrDefault(x => x.Id == stageId && x.IntentId == intentId)
                     ?? throw new ArgumentException($"stage_id not found: {stageId}");
        entity.Status = status.Trim().ToLowerInvariant();
        entity.UpdatedUtc = DateTimeOffset.UtcNow;
        db.SaveChanges();
        return new StageSetStatusResult(entity.Id, entity.Status);
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
            return StageJobDto(entity);
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
        if (state.ActiveIntentId is { } iid)
            intentTitle = db.Intents.AsNoTracking().Where(x => x.Id == iid).Select(x => x.Title).FirstOrDefault();
        if (state.ActiveSceneId is { } sid)
            sceneName = db.Scenes.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).FirstOrDefault();

        return new
        {
            database_path = state.DatabasePath,
            active_intent_id = state.ActiveIntentId,
            active_intent_title = intentTitle,
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
