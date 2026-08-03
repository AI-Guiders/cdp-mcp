using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;
public sealed partial class IdeTaskManagerTitlePrecedenceTests
{
    [Fact]
    public void FindIntent_query_with_chrome_does_not_match_bare_twin()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-chrome-twin-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            var bare = store.IntentUpsert(state, "Standalone CDP without Cursor host by 15.08", null);
            Assert.Null(store.FindIntentIdByTitle("Standalone CDP without Cursor host by 15.08 @act #CDP"));
            var tagged = store.IntentUpsert(state, "Standalone CDP without Cursor host by 15.08 @act #CDP", null);
            Assert.Equal(tagged.intent_id, store.FindIntentIdByTitle("Standalone CDP without Cursor host by 15.08 @act #CDP"));
            Assert.NotEqual(bare.intent_id, tagged.intent_id);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Done_by_feature_title_strips_board_chrome()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-feat-chrome-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState
            {
                DatabasePath = path
            };
            store.IntentUpsert(state, "invent ADX soft-warn peel conveyor @explore #CDP", null);
            var a = store.StageUpsert(state, "leaf-a", null, null, null).stage_id;
            store.FocusStage(state, a);
            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "invent ADX soft-warn peel conveyor" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("feature_done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
            Assert.Null(state.ActiveIntentId);
            using var db = Open(path);
            Assert.Equal("done", db.Stages.Single(s => s.Id == a).Status);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void Done_by_feature_title_closes_incomplete_leaves()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-feat-done-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState
            {
                DatabasePath = path
            };
            store.IntentUpsert(state, "invent ADX feature close", null);
            var a = store.StageUpsert(state, "leaf-a", null, null, null).stage_id;
            var b = store.StageUpsert(state, "leaf-b", null, null, null).stage_id;
            store.FocusStage(state, a);
            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "invent ADX feature close" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("feature_done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
            Assert.Null(state.ActiveIntentId);
            Assert.Null(state.ActiveStageId);
            using var db = Open(path);
            Assert.Equal("done", db.Stages.Single(s => s.Id == a).Status);
            Assert.Equal("done", db.Stages.Single(s => s.Id == b).Status);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void Shipped_without_start_starts_implicitly()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-ship-impl-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState
            {
                DatabasePath = path
            };
            store.IntentUpsert(state, "ship-feature", null);
            var id = store.StageUpsert(state, "ship-leaf", null, null, null).stage_id;
            store.FocusStage(state, id);
            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "shipped" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("mutation").GetProperty("started_implicit").GetBoolean());
            using var db = Open(path);
            var row = db.Stages.Single(s => s.Id == id);
            Assert.NotNull(row.StartedUtc);
            Assert.NotNull(row.CompletedUtc);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            { /* ignore */
            }
        }
    }

    [Fact]
    public void FindIntent_unique_prefix_does_not_steal_content_twin()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-prefix-twin-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            var twin = store.IntentUpsert(state, "Dig densest SoftFL CLOSED after FeatureDone live", null);
            // Truncated shared stem — used to unique-prefix onto the Dig densest twin.
            Assert.Null(store.FindIntentIdByTitle("Dig densest SoftFL CLOSED after FeatureDone"));
            Assert.Equal(twin.intent_id, store.FindIntentIdByTitle("Dig densest SoftFL CLOSED after FeatureDone live"));
            // Chrome query must not silently land on bare Dig densest twin (0.5.553).
            Assert.Null(store.FindIntentIdByTitle("Dig densest SoftFL CLOSED after FeatureDone live @act #CDP"));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Shipped_foreign_feature_preserves_active_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-ship-foreign-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            var invent = store.IntentUpsert(state, "invent dig focus", null);
            var inventLeaf = store.StageUpsert(state, "invent-leaf", null, null, null).stage_id;
            store.FocusStage(state, inventLeaf);

            store.IntentUpsert(state, "WitDB torn quarantine auto-heal", null);
            store.IntentSelect(state, invent.intent_id);
            store.FocusStage(state, inventLeaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "shipped", title = "WitDB torn quarantine auto-heal" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            var mut = doc.RootElement.GetProperty("mutation");
            Assert.Equal("feature_shipped", mut.GetProperty("op").GetString());
            Assert.False(mut.GetProperty("focus_cleared").GetBoolean());
            Assert.Equal(invent.intent_id, state.ActiveIntentId);
            Assert.Equal(inventLeaf, state.ActiveStageId);

            using var db = Open(path);
            Assert.Equal("active", db.Stages.Single(s => s.Id == inventLeaf).Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }


    static Dictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    static IntentWorkspaceStore BootStore(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>().UseWitDb($"Data Source={path}").Options;
        using (var boot = new IntentWorkspaceDbContext(opts))
            boot.Database.EnsureCreated();
        var store = new IntentWorkspaceStore(opts, path);
        store.EnsureStageClockColumns();
        store.EnsureStageEventsTable();
        return store;
    }

    static IntentWorkspaceDbContext Open(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>().UseWitDb($"Data Source={path}").Options;
        return new IntentWorkspaceDbContext(opts);
    }
}