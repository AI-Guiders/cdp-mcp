using Xunit;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace CdpMcp.Tests;

public sealed class StageEventLedgerTests
{
    [Fact]
    public void Note_then_list_roundtrips_on_fresh_witdb()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-se-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "se-ledger-feature", null);
            var stageId = store.StageUpsert(state, "se-ledger-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);
            store.StageClockStart(state, stageId);

            store.StageEventNote(state, stageId, "hello ledger");

            var json = System.Text.Json.JsonSerializer.Serialize(store.StageEventList(state, stageId));
            Assert.Contains("\"count\":1", json);
            Assert.Contains("hello ledger", json);
            Assert.Equal(1, store.StageEventCounts(stageId).Note);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Legacy_text_schema_is_recreated_so_guid_filter_works()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-se-leg-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
                .UseWitDb($"Data Source={path}")
                .Options;
            using (var boot = new IntentWorkspaceDbContext(opts))
                boot.Database.EnsureCreated();

            using (var db = new IntentWorkspaceDbContext(opts))
            {
                db.Database.ExecuteSqlRaw(
                    """
                    DROP TABLE IF EXISTS stage_events;
                    DROP TABLE IF EXISTS stage_events_v2;
                    CREATE TABLE stage_events (
                        Id TEXT NOT NULL PRIMARY KEY,
                        StageId TEXT NOT NULL,
                        Utc TEXT NOT NULL,
                        Kind TEXT NOT NULL,
                        Source TEXT NOT NULL,
                        Summary TEXT NOT NULL,
                        Ref TEXT NULL
                    );
                    """);
            }

            var store = new IntentWorkspaceStore(opts, path);
            store.EnsureStageClockColumns();
            store.EnsureStageEventsTable();

            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "se-legacy-feature", null);
            var stageId = store.StageUpsert(state, "se-legacy-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);
            store.StageClockStart(state, stageId);

            store.StageEventNote(state, stageId, "after migrate");
            var json = System.Text.Json.JsonSerializer.Serialize(store.StageEventList(state, stageId));
            Assert.Contains("\"count\":1", json);
            Assert.Contains("after migrate", json);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureStageClockColumns();
        store.EnsureStageEventsTable();
        return store;
    }
}
