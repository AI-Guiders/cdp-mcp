#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Lived 2026-08-05: plan pulse tax — Handle + CollectWork + PublishGlass must share one BuildBoard.</summary>
public sealed class IdeTaskManagerBoardCacheTests
{
    [Fact]
    public void BuildBoard_reuses_cache_for_same_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-cache-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            IdeTaskManager.InvalidateBoardCache();
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "cache-feature", null);
            var stageId = store.StageUpsert(state, "cache-leaf", null, null, null).stage_id;
            store.FocusStage(state, stageId);

            var a = IdeTaskManager.BuildBoard(store, state, "explore");
            Assert.Equal(0, IdeTaskManager.BoardCacheHits);
            var b = IdeTaskManager.BuildBoard(store, state, "explore");
            Assert.Equal(1, IdeTaskManager.BoardCacheHits);
            Assert.Equal(a.Pulse, b.Pulse);

            IdeTaskManager.Handle(store, state, Args(new { tm_op = "start" }));
            var c = IdeTaskManager.BuildBoard(store, state, "explore");
            Assert.Equal(0, IdeTaskManager.BoardCacheHits);
        }
        finally
        {
            IdeTaskManager.InvalidateBoardCache();
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

    static IReadOnlyDictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }
}
