using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeChangePlannerTests
{
    [Fact]
    public void Seed_anchor_ack_marks_hybrid_dor_met()
    {
        var wit = Path.Combine(Path.GetTempPath(), "cdp-cp-" + Guid.NewGuid().ToString("N") + ".witdb");
        var planDir = Path.Combine(Path.GetTempPath(), "cdp-cp-plans-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = BootStore(wit);
            var state = new IntentWorkspaceState { DatabasePath = wit };
            store.IntentUpsert(state, "cp-feature", null);
            var stageId = store.StageUpsert(state, "cp-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);

            var seed = IdeChangePlanner.Handle(store, state, null, Args(new
            {
                cp_op = "seed",
                dir = planDir
            }));
            using (var seedDoc = JsonDocument.Parse(JsonSerializer.Serialize(seed)))
            {
                Assert.True(seedDoc.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("hybrid", seedDoc.RootElement.GetProperty("criterion").GetProperty("mode").GetString());
                Assert.Equal("pending", seedDoc.RootElement.GetProperty("criterion").GetProperty("status").GetString());
                Assert.False(seedDoc.RootElement.GetProperty("auto_ok").GetBoolean());
            }

            var anchored = IdeChangePlanner.Handle(store, state, null, Args(new
            {
                cp_op = "anchor",
                dir = planDir,
                anchor = "[F:IdeChangePlanner.cs;M:Handle]"
            }));
            using (var aDoc = JsonDocument.Parse(JsonSerializer.Serialize(anchored)))
            {
                Assert.True(aDoc.RootElement.GetProperty("auto_ok").GetBoolean());
                Assert.True(aDoc.RootElement.GetProperty("needs_manual").GetBoolean());
                Assert.False(aDoc.RootElement.GetProperty("ready").GetBoolean());
                Assert.Equal("pending", aDoc.RootElement.GetProperty("criterion").GetProperty("status").GetString());
                Assert.StartsWith("change_plan:", aDoc.RootElement.GetProperty("evidence_ref").GetString());
            }

            var acked = IdeChangePlanner.Handle(store, state, null, Args(new
            {
                cp_op = "ack",
                dir = planDir
            }));
            using var ackDoc = JsonDocument.Parse(JsonSerializer.Serialize(acked));
            Assert.True(ackDoc.RootElement.GetProperty("ready").GetBoolean());
            Assert.Equal("met", ackDoc.RootElement.GetProperty("criterion").GetProperty("status").GetString());
        }
        finally
        {
            try { File.Delete(wit); } catch { /* ignore */ }
            try { if (Directory.Exists(planDir)) Directory.Delete(planDir, true); } catch { /* ignore */ }
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
        store.EnsureStageCriteriaTable();
        return store;
    }
}
