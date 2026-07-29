using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeTaskManagerTitlePrecedenceTests
{
    [Fact]
    public void Done_by_title_does_not_close_active_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-prec-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "prec-feature", null);
            var activeId = store.StageUpsert(state, "active-criteria", null, null, null).stage_id;
            var otherId = store.StageUpsert(state, "deferred-seed", null, null, null).stage_id;
            store.FocusStage(state, activeId);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "done",
                title = "deferred-seed"
            }));

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            var mutation = doc.RootElement.GetProperty("mutation");
            Assert.Equal(otherId.ToString(), mutation.GetProperty("task_id").GetGuid().ToString());
            Assert.Equal("done", mutation.GetProperty("status").GetString());
            Assert.Equal(activeId, state.ActiveStageId);

            using var db = Open(path);
            Assert.Equal("active", db.Stages.Single(s => s.Id == activeId).Status);
            Assert.Equal("done", db.Stages.Single(s => s.Id == otherId).Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Drop_by_title_does_not_delete_active_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-drop-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "drop-feature", null);
            var activeId = store.StageUpsert(state, "keep-active", null, null, null).stage_id;
            var otherId = store.StageUpsert(state, "drop-me", null, null, null).stage_id;
            store.FocusStage(state, activeId);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "drop",
                title = "drop-me"
            }));

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(activeId, state.ActiveStageId);

            using var db = Open(path);
            Assert.True(db.Stages.Any(s => s.Id == activeId));
            Assert.False(db.Stages.Any(s => s.Id == otherId));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Focus_by_title_with_slash_finds_stage()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-slash-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "slash-feature", null);
            var activeId = store.StageUpsert(state, "keep-active", null, null, null).stage_id;
            var slashTitle = "Add deferred/parked seed without stealing focus";
            var slashId = store.StageUpsert(state, slashTitle, null, null, null).stage_id;
            store.FocusStage(state, activeId);
            store.StageSetStatus(state, slashId, "parked");

            Assert.Equal(slashId, store.FindStageIdByTitle(state, slashTitle));

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "focus",
                title = slashTitle
            }));

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(slashId, state.ActiveStageId);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Defer_new_title_preserves_active_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-defer-seed-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "defer-feature", null);
            var activeId = store.StageUpsert(state, "keep-active", null, null, null).stage_id;
            store.FocusStage(state, activeId);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "defer",
                title = "new-deferred-seed"
            }));

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(activeId, state.ActiveStageId);

            using var db = Open(path);
            Assert.Equal("deferred", db.Stages.Single(s => s.Title == "new-deferred-seed").Status);
            Assert.Equal("active", db.Stages.Single(s => s.Id == activeId).Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Park_new_title_preserves_active_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-park-seed-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "park-feature", null);
            var activeId = store.StageUpsert(state, "keep-active", null, null, null).stage_id;
            store.FocusStage(state, activeId);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "park",
                title = "new-parked-seed"
            }));

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(activeId, state.ActiveStageId);

            using var db = Open(path);
            Assert.Equal("parked", db.Stages.Single(s => s.Title == "new-parked-seed").Status);
            Assert.Equal("active", db.Stages.Single(s => s.Id == activeId).Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static Dictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
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

    static IntentWorkspaceDbContext Open(string path)
    {
        var opts = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        return new IntentWorkspaceDbContext(opts);
    }
}
