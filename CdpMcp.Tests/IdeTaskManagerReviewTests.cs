#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Operator Review Results — save on leaf, dig before done (gap 3.8 afferent slice).</summary>
public sealed class IdeTaskManagerReviewTests
{
    [Fact]
    public void Review_add_list_ack_and_done_refuse_until_clear()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-review-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "review-feature", null);
            var leaf = store.StageUpsert(state, "review-leaf", null, null, null).stage_id;
            store.FocusStage(state, leaf);
            IdeTaskManager.Handle(store, state, Args(new { tm_op = "start" }));

            var add = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "review",
                text = "ModelPicker still hard to scan on dark"
            }));
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(add)))
            {
                Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
                var mut = doc.RootElement.GetProperty("mutation");
                Assert.Equal("review", mut.GetProperty("op").GetString());
                Assert.Equal("add", mut.GetProperty("action").GetString());
                Assert.Equal(1, mut.GetProperty("open").GetInt32());
                var reviewId = mut.GetProperty("review_id").GetGuid();

                var list = IdeTaskManager.Handle(store, state, Args(new { tm_op = "review", action = "list" }));
                using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
                Assert.Equal(1, listDoc.RootElement.GetProperty("mutation").GetProperty("open").GetInt32());

                var refuse = Assert.Throws<ArgumentException>(() =>
                    IdeReviewShield.RefuseDoneWithOpenReviews(store, leaf, null));
                Assert.Contains(IdeReviewShield.RefuseId, refuse.Message, StringComparison.Ordinal);

                IdeTaskManager.Handle(store, state, Args(new
                {
                    tm_op = "review",
                    action = "ack",
                    review_id = reviewId.ToString("N")
                }));
                Assert.Equal(0, store.StageEventOpenReviewCount(leaf));
                IdeReviewShield.RefuseDoneWithOpenReviews(store, leaf, null); // no throw
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Review_repl_title_routes_add()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-tm-review-repl-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "repl-feature", null);
            var leaf = store.StageUpsert(state, "repl-leaf", null, null, null).stage_id;
            store.FocusStage(state, leaf);
            IdeTaskManager.Handle(store, state, Args(new { tm_op = "start" }));

            var applied = IdeRepl.Apply("review sticky Who defaults must stay Operator", new Dictionary<string, JsonElement>());
            Assert.NotNull(applied);
            Assert.Null(applied.Value.Direct);
            var result = IdeTaskManager.Handle(store, state, applied.Value.Args);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("add", doc.RootElement.GetProperty("mutation").GetProperty("action").GetString());
            Assert.Equal(1, store.StageEventOpenReviewCount(leaf));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
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

    static IReadOnlyDictionary<string, JsonElement> Args(object anon)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(anon));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }
}
