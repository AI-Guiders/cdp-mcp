using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed partial class IntentWorkspaceStore
{
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
            if (row.ActiveStageId is { } sid && db.Stages.AsNoTracking().Any(x => x.Id == sid && (state.ActiveIntentId == null || x.IntentId == state.ActiveIntentId)))
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
                db.WorkFocus.Add(new WorkFocusEntity { Id = 1, ActiveIntentId = state.ActiveIntentId, ActiveStageId = state.ActiveStageId, UpdatedUtc = now });
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
}