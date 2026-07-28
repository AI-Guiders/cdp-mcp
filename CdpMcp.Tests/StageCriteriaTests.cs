using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class StageCriteriaTests
{
    [Fact]
    public void Add_list_met_roundtrips_on_fresh_witdb()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-sc-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "criteria-feature", null);
            var stageId = store.StageUpsert(state, "criteria-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);

            var added = store.StageCriterionAdd(
                state, stageId, "dor", "Blast radius understood", "hybrid", "planner:demo");
            using var addDoc = JsonDocument.Parse(JsonSerializer.Serialize(added));
            Assert.Equal("dor", addDoc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("hybrid", addDoc.RootElement.GetProperty("mode").GetString());
            Assert.Equal("pending", addDoc.RootElement.GetProperty("status").GetString());
            var cid = addDoc.RootElement.GetProperty("criterion_id").GetGuid();

            store.StageCriterionAdd(state, stageId, "ac", "Board shows criteria summary", "manual", null);
            store.StageCriterionAdd(state, stageId, "dod", "Focused tests green", "manual", null);

            var list = store.StageCriterionList(state, stageId);
            using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
            Assert.Equal(3, listDoc.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(3, listDoc.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
            Assert.Equal(0, listDoc.RootElement.GetProperty("summary").GetProperty("met").GetInt32());

            store.StageCriterionSetStatus(state, cid, "met", "planner:ok");
            var after = store.StageCriterionList(state, stageId, "dor");
            using var afterDoc = JsonDocument.Parse(JsonSerializer.Serialize(after));
            Assert.Equal(1, afterDoc.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("met", afterDoc.RootElement.GetProperty("criteria")[0].GetProperty("status").GetString());

            var stageGet = store.StageGet(stageId);
            using var getDoc = JsonDocument.Parse(JsonSerializer.Serialize(stageGet));
            Assert.Equal(3, getDoc.RootElement.GetProperty("criteria_summary").GetProperty("total").GetInt32());
            Assert.Equal(1, getDoc.RootElement.GetProperty("criteria_summary").GetProperty("met").GetInt32());
            Assert.Equal(3, getDoc.RootElement.GetProperty("criteria").GetArrayLength());
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Drop_stage_cascades_criteria()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-sc-drop-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "drop-feature", null);
            var stageId = store.StageUpsert(state, "drop-task", null, null, null).stage_id;
            store.FocusStage(state, stageId);
            store.StageCriterionAdd(state, stageId, "ac", "temp", null, null);

            store.StageDelete(state, stageId);

            using var db = Open(path);
            Assert.False(db.Stages.Any(s => s.Id == stageId));
            Assert.False(db.StageCriteria.Any(c => c.StageId == stageId));
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
        store.EnsureStageCriteriaTable();
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
