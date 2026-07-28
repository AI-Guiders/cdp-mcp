using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed class IntentWorkspaceState
{
    public Guid? ActiveIntentId { get; set; }
    public Guid? ActiveSceneId { get; set; }
    public Guid? ActiveStageId { get; set; }
    public string DatabasePath { get; set; } = "";
}

internal sealed partial class IntentWorkspaceStore(DbContextOptions<IntentWorkspaceDbContext> options)
{
    private static readonly Lock DbGate = new();
    private static readonly HashSet<string> StageStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "active", "done", "parked"
    };

    private IntentWorkspaceDbContext Open() => new(options);

    /// <summary>Serialize WitDB access. Brief retry softens transient cross-call / dual-seat file locks.</summary>
    private T WithDb<T>(Func<IntentWorkspaceDbContext, T> action)
    {
        const int attempts = 4;
        for (var i = 0; ; i++)
        {
            try
            {
                lock (DbGate)
                {
                    using var db = Open();
                    return action(db);
                }
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(25 * (i + 1));
            }
            catch (Exception ex) when (i < attempts - 1 && IsTransientDbLock(ex))
            {
                Thread.Sleep(25 * (i + 1));
            }
        }
    }

    private void WithDb(Action<IntentWorkspaceDbContext> action)
    {
        WithDb<object?>(db =>
        {
            action(db);
            return null;
        });
    }

    static bool IsTransientDbLock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
                || m.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || m.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || m.Contains("locking", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

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
        var row = db.Stages.FirstOrDefault(x => x.Id == stageId);
        if (row is not null)
            db.Stages.Remove(row);
    }

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
                .Select(x => new
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
                    job_error = x.JobError
                })
                .ToList();
            return (object)new { intent_id = intentId, stages = rows };
        });
    }

    public StageSetStatusResult StageSetStatus(IntentWorkspaceState state, Guid stageId, string status)
    {
        if (!StageStatuses.Contains(status))
            throw new ArgumentException("status must be pending|active|done|parked.");
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
                events_suffix = events.Length == 0 ? null : events.TrimStart(' ', '·').Trim(),
                kind = "wall",
                hint = "SA tempo (wall) — not a score; events=pointers not reward"
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
    public void MigrateLegacyDeskSeatsJsonIfPresent()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "desk-seats.json");
        if (!File.Exists(legacy))
            return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(legacy));
            var root = doc.RootElement;
            if (!root.TryGetProperty("seats", out var seatsEl) && !root.TryGetProperty("Seats", out seatsEl))
            {
                File.Delete(legacy);
                return;
            }

            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in seatsEl.EnumerateObject())
                map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (map.Count > 0)
                DeskSeatsSave(map);
            File.Delete(legacy);
        }
        catch
        {
            // leave legacy if parse failed
        }
    }

    public void EnsureWorkFocusTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS work_focus (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    ActiveIntentId TEXT NULL,
                    ActiveStageId TEXT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """);
        });
    }

    public void WorkFocusHydrate(IntentWorkspaceState state)
    {
        WithDb(db =>
        {
            var row = db.WorkFocus.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (row is null)
                return;
            if (row.ActiveIntentId is { } iid && db.Intents.AsNoTracking().Any(x => x.Id == iid))
                state.ActiveIntentId = iid;
            if (row.ActiveStageId is { } sid
                && db.Stages.AsNoTracking().Any(x => x.Id == sid
                    && (state.ActiveIntentId == null || x.IntentId == state.ActiveIntentId)))
                state.ActiveStageId = sid;
        });
    }

    public void WorkFocusSave(IntentWorkspaceState state)
    {
        var now = DateTimeOffset.UtcNow;
        WithDb(db =>
        {
            var row = db.WorkFocus.Find(1);
            if (row is null)
            {
                db.WorkFocus.Add(new WorkFocusEntity
                {
                    Id = 1,
                    ActiveIntentId = state.ActiveIntentId,
                    ActiveStageId = state.ActiveStageId,
                    UpdatedUtc = now
                });
            }
            else
            {
                row.ActiveIntentId = state.ActiveIntentId;
                row.ActiveStageId = state.ActiveStageId;
                row.UpdatedUtc = now;
            }

            db.SaveChanges();
        });
    }

    /// <summary>Existing WitDB may predate script_last_run — EnsureCreated won't alter.</summary>
    public void EnsureScriptLastRunTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw(
                """
                CREATE TABLE IF NOT EXISTS script_last_run (
                    RootKey TEXT NOT NULL PRIMARY KEY,
                    Path TEXT NOT NULL,
                    Mode TEXT NOT NULL,
                    Ok INTEGER NOT NULL,
                    AtUtc TEXT NOT NULL,
                    Pulse TEXT NOT NULL,
                    BodyJson TEXT NULL,
                    BoardJson TEXT NOT NULL
                );
                """);
        });
    }

    public void ScriptLastRunSave(
        string rootKey,
        string path,
        string mode,
        bool ok,
        DateTime atUtc,
        string pulse,
        string? bodyJson,
        IReadOnlyList<string> board)
    {
        var boardJson = JsonSerializer.Serialize(board);
        var at = new DateTimeOffset(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));
        WithDb(db =>
        {
            var row = db.ScriptLastRuns.Find(rootKey);
            if (row is null)
            {
                db.ScriptLastRuns.Add(new ScriptLastRunEntity
                {
                    RootKey = rootKey,
                    Path = path,
                    Mode = mode,
                    Ok = ok,
                    AtUtc = at,
                    Pulse = pulse,
                    BodyJson = bodyJson,
                    BoardJson = boardJson
                });
            }
            else
            {
                row.Path = path;
                row.Mode = mode;
                row.Ok = ok;
                row.AtUtc = at;
                row.Pulse = pulse;
                row.BodyJson = bodyJson;
                row.BoardJson = boardJson;
            }

            db.SaveChanges();
        });
    }

    public (string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, string[] Board)?
        ScriptLastRunTryLoad(string rootKey)
    {
        return WithDb<(string Path, string Mode, bool Ok, DateTime AtUtc, string Pulse, string? BodyJson, string[] Board)?>(db =>
        {
            var row = db.ScriptLastRuns.AsNoTracking().FirstOrDefault(x => x.RootKey == rootKey);
            if (row is null)
                return null;

            string[] board;
            try
            {
                board = JsonSerializer.Deserialize<string[]>(row.BoardJson) ?? [];
            }
            catch
            {
                board = [row.Pulse];
            }

            return (row.Path, row.Mode, row.Ok, row.AtUtc.UtcDateTime, row.Pulse, row.BodyJson, board);
        });
    }

    public void FocusStage(IntentWorkspaceState state, Guid stageId)
    {
        WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            state.ActiveIntentId = entity.IntentId;
            state.ActiveStageId = entity.Id;
            foreach (var s in db.Stages.Where(x => x.IntentId == entity.IntentId && x.Status == "active"))
            {
                if (s.Id != entity.Id)
                    s.Status = "pending";
            }

            entity.Status = "active";
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
        });
        WorkFocusSave(state);
    }

    public Guid? FindIntentIdByTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var t = title.Trim();
        return WithDb(db =>
            db.Intents.AsNoTracking()
                .Where(x => x.Title == t)
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault()
            ?? db.Intents.AsNoTracking()
                .Where(x => x.Title.ToLower() == t.ToLower())
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault());
    }

    public Guid? FindStageIdByTitle(IntentWorkspaceState state, string? title) =>
        FindStageMatching(state, title, parentId: null, matchParent: false);

    /// <summary>
    /// When <paramref name="matchParent"/> is true, match <paramref name="parentId"/> exactly
    /// (null = root). Otherwise ignore parent.
    /// </summary>
    public Guid? FindStageMatching(
        IntentWorkspaceState state,
        string? title,
        Guid? parentId,
        bool matchParent)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var t = title.Trim();
        var intentId = state.ActiveIntentId;
        return WithDb(db =>
        {
            var q = db.Stages.AsNoTracking().AsQueryable();
            if (intentId is { } iid)
                q = q.Where(x => x.IntentId == iid);
            if (matchParent)
                q = q.Where(x => x.ParentId == parentId);

            return q.Where(x => x.Title == t)
                       .OrderByDescending(x => x.UpdatedUtc)
                       .Select(x => (Guid?)x.Id)
                       .FirstOrDefault()
                   ?? q.Where(x => x.Title.ToLower() == t.ToLower())
                       .OrderByDescending(x => x.UpdatedUtc)
                       .Select(x => (Guid?)x.Id)
                       .FirstOrDefault();
        });
    }

    public Guid? FindNextPendingStage(IntentWorkspaceState state)
    {
        if (state.ActiveIntentId is not { } intentId)
            return null;
        return WithDb(db =>
            db.Stages.AsNoTracking()
                .Where(x => x.IntentId == intentId
                            && (x.Status == "pending" || x.Status == "active")
                            && x.Id != state.ActiveStageId)
                .OrderBy(x => x.Ordinal)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefault());
    }

    public IdeTaskManager.Snapshot TaskManagerSnapshot(IntentWorkspaceState state)
    {
        return WithDb(db =>
        {
            var intents = db.Intents.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).ToList();
            var stages = db.Stages.AsNoTracking().ToList();
            string? activeFeatureTitle = null;
            string? activeStageTitle = null;
            string? activeStagePhase = null;
            DateTimeOffset? activeStageStarted = null;
            DateTimeOffset? activeStageCompleted = null;
            if (state.ActiveIntentId is { } aid)
                activeFeatureTitle = intents.FirstOrDefault(x => x.Id == aid)?.Title;
            if (state.ActiveStageId is { } sid)
            {
                var st = stages.FirstOrDefault(x => x.Id == sid);
                activeStageTitle = st?.Title;
                activeStagePhase = st?.PhaseAffinity;
                activeStageStarted = st?.StartedUtc;
                activeStageCompleted = st?.CompletedUtc;
            }

            var features = intents.Select(i =>
            {
                var st = stages.Where(s => s.IntentId == i.Id)
                    .Select(s => new IdeTaskManager.StageNode(
                        s.Id, s.ParentId, s.Title, s.Status, s.Ordinal, s.PhaseAffinity,
                        s.StartedUtc, s.CompletedUtc))
                    .ToList();
                return new IdeTaskManager.FeatureNode(
                    i.Id,
                    i.Title,
                    state.ActiveIntentId == i.Id,
                    state.ActiveIntentId == i.Id ? state.ActiveStageId : null,
                    st);
            }).ToList();

            return new IdeTaskManager.Snapshot(
                state.ActiveIntentId,
                activeFeatureTitle,
                state.ActiveStageId,
                activeStageTitle,
                activeStagePhase,
                activeStageStarted,
                activeStageCompleted,
                features);
        });
    }
}
