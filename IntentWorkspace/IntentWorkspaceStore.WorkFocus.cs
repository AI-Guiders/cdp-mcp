#nullable enable
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
    /// <summary>Legacy/unbound lane when tip Who is unknown.</summary>
    public const string DefaultFocusLane = "_default_";

    public void EnsureWorkFocusTable()
    {
        WithDb(db =>
        {
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS work_focus (
                    Id INTEGER NOT NULL PRIMARY KEY,
                    ActiveIntentId TEXT NULL,
                    ActiveStageId TEXT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """);
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS work_focus_lanes (
                    Lane TEXT NOT NULL PRIMARY KEY,
                    ActiveIntentId TEXT NULL,
                    ActiveStageId TEXT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """);
        });
        MigrateLegacyWorkFocusIntoDefaultLane();
    }

    void MigrateLegacyWorkFocusIntoDefaultLane()
    {
        WithDb(db =>
        {
            if (db.WorkFocusLanes.AsNoTracking().Any())
                return;
            var legacy = db.WorkFocus.AsNoTracking().FirstOrDefault(x => x.Id == 1);
            if (legacy is null || (legacy.ActiveIntentId is null && legacy.ActiveStageId is null))
                return;
            db.WorkFocusLanes.Add(new WorkFocusLaneEntity
            {
                Lane = DefaultFocusLane,
                ActiveIntentId = legacy.ActiveIntentId,
                ActiveStageId = legacy.ActiveStageId,
                UpdatedUtc = legacy.UpdatedUtc
            });
            db.SaveChanges();
        });
    }

    /// <summary>Who lane from explicit arg, else PF tip Who, else <see cref="DefaultFocusLane"/>.</summary>
    public static string ResolveFocusLane(string? explicitLane = null)
    {
        var n = NormalizeExecutor(explicitLane);
        if (n is not null)
            return n;
        try
        {
            var tip = CideIntercomIdentityLatch.TrySeat(CideIntercomVoiceLatch.SeatPf);
            if (tip?.Name is { Length: > 0 } name)
            {
                var who = NormalizeExecutor(name);
                if (who is not null)
                    return who;
            }
        }
        catch
        {
            /* tip latch optional in tests */
        }

        return DefaultFocusLane;
    }

    public void WorkFocusHydrate(IntentWorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(state.FocusLane))
            state.FocusLane = ResolveFocusLane();

        WithDb(db =>
        {
            var lane = state.FocusLane;
            var row = db.WorkFocusLanes.AsNoTracking()
                .FirstOrDefault(x => x.Lane == lane);
            if (row is null && lane != DefaultFocusLane)
            {
                var legacy = db.WorkFocus.AsNoTracking().FirstOrDefault(x => x.Id == 1);
                if (legacy is not null)
                    row = new WorkFocusLaneEntity
                    {
                        Lane = lane,
                        ActiveIntentId = legacy.ActiveIntentId,
                        ActiveStageId = legacy.ActiveStageId,
                        UpdatedUtc = legacy.UpdatedUtc
                    };
            }

            if (row is null)
                return;

            if (row.ActiveIntentId is { } iid && db.Intents.AsNoTracking().Any(x => x.Id == iid))
                state.ActiveIntentId = iid;
            if (row.ActiveStageId is { } sid
                && db.Stages.AsNoTracking().Any(x =>
                    x.Id == sid && (state.ActiveIntentId == null || x.IntentId == state.ActiveIntentId)))
                state.ActiveStageId = sid;
        });
    }

    public void WorkFocusSave(IntentWorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(state.FocusLane))
            state.FocusLane = ResolveFocusLane();

        var now = DateTimeOffset.UtcNow;
        var lane = state.FocusLane;
        WithDb(db =>
        {
            var row = db.WorkFocusLanes.Find(lane);
            if (row is null)
            {
                db.WorkFocusLanes.Add(new WorkFocusLaneEntity
                {
                    Lane = lane,
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

            var legacy = db.WorkFocus.Find(1);
            if (legacy is null)
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
                legacy.ActiveIntentId = state.ActiveIntentId;
                legacy.ActiveStageId = state.ActiveStageId;
                legacy.UpdatedUtc = now;
            }

            db.SaveChanges();
        });
    }

    /// <summary>Stage ids focused on other Who lanes (board [»] · FocusStage protect).</summary>
    public HashSet<Guid> WorkFocusOtherLaneStageIds(IntentWorkspaceState state)
    {
        var lane = string.IsNullOrWhiteSpace(state.FocusLane) ? ResolveFocusLane() : state.FocusLane;
        return WithDb(db =>
        {
            var set = new HashSet<Guid>();
            foreach (var row in db.WorkFocusLanes.AsNoTracking()
                         .Where(x => x.Lane != lane && x.ActiveStageId != null))
            {
                if (row.ActiveStageId is { } sid)
                    set.Add(sid);
            }

            return set;
        });
    }

    /// <summary>Switch Who lane: save current, hydrate target (shared board).</summary>
    public void WorkFocusSwitchLane(IntentWorkspaceState state, string? laneRaw)
    {
        WorkFocusSave(state);
        state.FocusLane = ResolveFocusLane(laneRaw);
        state.ActiveIntentId = null;
        state.ActiveStageId = null;
        WorkFocusHydrate(state);
        if (state.ActiveStageId is { } sid)
            FocusStagePreserveOtherLanes(state, sid);
        else
            WorkFocusSave(state);
    }

    /// <summary>Focus stage without demoting other Who lanes' active status.</summary>
    public void FocusStagePreserveOtherLanes(IntentWorkspaceState state, Guid stageId)
    {
        var protect = WorkFocusOtherLaneStageIds(state);
        WithDb(db =>
        {
            var entity = db.Stages.FirstOrDefault(x => x.Id == stageId)
                         ?? throw new ArgumentException($"stage_id not found: {stageId}");
            state.ActiveIntentId = entity.IntentId;
            state.ActiveStageId = entity.Id;
            foreach (var s in db.Stages.Where(x => x.IntentId == entity.IntentId && x.Status == "active"))
            {
                if (s.Id != entity.Id && !protect.Contains(s.Id))
                    s.Status = "pending";
            }

            entity.Status = "active";
            entity.UpdatedUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
        });
        WorkFocusSave(state);
    }
}
