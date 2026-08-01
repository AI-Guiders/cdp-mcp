using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
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
        var bare = StripBoardChrome(t);
        var intentId = state.ActiveIntentId;
        return WithDb(db =>
        {
            var q = db.Stages.AsNoTracking().AsQueryable();
            if (intentId is { } iid)
                q = q.Where(x => x.IntentId == iid);
            if (matchParent)
                q = q.Where(x => x.ParentId == parentId);

            // Materialize before title compare — WitDB provider equality can miss titles with '/' (board shows them; focus/done by title fails).
            var list = q.ToList();

            Guid? Pick(Func<StageEntity, bool> pred) =>
                list.Where(pred)
                    .OrderByDescending(x => x.UpdatedUtc)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefault();

            // Exact (with/without board chrome @phase #Product baked into stored title or pasted query).
            var hit = Pick(x => x.Title == t)
                ?? Pick(x => string.Equals(x.Title, t, StringComparison.OrdinalIgnoreCase))
                ?? (bare.Length > 0 && bare != t
                    ? Pick(x => x.Title == bare)
                        ?? Pick(x => string.Equals(x.Title, bare, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? Pick(x => string.Equals(StripBoardChrome(x.Title), bare, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;

            // Unique prefix — stop junk seeds from `task Add deferred` when a longer slash title already exists.
            if (bare.Length < 8)
                return null;
            var prefix = list
                .Where(x =>
                {
                    var stored = StripBoardChrome(x.Title);
                    return stored.StartsWith(bare, StringComparison.OrdinalIgnoreCase)
                           || x.Title.StartsWith(t, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(x => x.UpdatedUtc)
                .ToList();
            return prefix.Count == 1 ? prefix[0].Id : null;
        });
    }

    /// <summary>
    /// Peel trailing board chrome (<c>@act</c>/<c>@todo</c>/<c>#CDP</c>) so drop/focus pasted from the board matches the stored title.
    /// </summary>
    internal static string StripBoardChrome(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        while (words.Count > 0)
        {
            var last = words[^1];
            if ((last.StartsWith('@') || last.StartsWith('#')) && last.Length > 1)
            {
                words.RemoveAt(words.Count - 1);
                continue;
            }

            break;
        }

        return string.Join(' ', words);
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
                        s.StartedUtc, s.CompletedUtc, s.Product))
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
