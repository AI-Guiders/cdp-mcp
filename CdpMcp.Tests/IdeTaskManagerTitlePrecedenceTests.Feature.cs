using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;
public sealed partial class IdeTaskManagerTitlePrecedenceTests
{
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