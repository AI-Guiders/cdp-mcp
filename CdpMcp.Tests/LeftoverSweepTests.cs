using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public sealed class LeftoverSweepTests
{
    [Fact]
    public void IsAcDodShipReady_requires_nonempty_ac_and_dod_all_met()
    {
        Assert.False(IntentWorkspaceStore.IsAcDodShipReady([]));

        var onlyAc = new List<StageCriterionEntity>
        {
            Row("ac", "met"),
            Row("dor", "met")
        };
        Assert.False(IntentWorkspaceStore.IsAcDodShipReady(onlyAc));

        var ready = new List<StageCriterionEntity>
        {
            Row("ac", "met"),
            Row("dod", "waived"),
            Row("dor", "pending")
        };
        Assert.True(IntentWorkspaceStore.IsAcDodShipReady(ready));

        var unmetAc = new List<StageCriterionEntity>
        {
            Row("ac", "pending"),
            Row("dod", "met")
        };
        Assert.False(IntentWorkspaceStore.IsAcDodShipReady(unmetAc));
    }

    [Fact]
    public void Dry_run_lists_parked_with_ac_dod_met_and_skips_vacuous()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-leftover-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "leftover-feature", null);

            var focusId = store.StageUpsert(state, "active focus task", null, null, null).stage_id;
            store.FocusStage(state, focusId);

            var readyId = store.StageUpsert(state, "parked ready leftover", null, null, null).stage_id;
            store.StageSetStatus(state, readyId, "parked");
            var ac = store.StageCriterionAdd(state, readyId, "ac", "Accept defer without focus steal", null, null);
            var dod = store.StageCriterionAdd(state, readyId, "dod", "Tests green", null, null);
            store.StageCriterionSetStatus(state, CritId(ac), "met");
            store.StageCriterionSetStatus(state, CritId(dod), "met");

            var vacuousId = store.StageUpsert(state, "parked vacuous", null, null, null).stage_id;
            store.StageSetStatus(state, vacuousId, "parked");

            var deferredReady = store.StageUpsert(state, "deferred ready", null, null, null).stage_id;
            store.StageSetStatus(state, deferredReady, "deferred");
            var ac2 = store.StageCriterionAdd(state, deferredReady, "ac", "AC2", null, null);
            var dod2 = store.StageCriterionAdd(state, deferredReady, "dod", "DoD2", null, null);
            store.StageCriterionSetStatus(state, CritId(ac2), "met");
            store.StageCriterionSetStatus(state, CritId(dod2), "waived");

            var dry = IdeTaskManager.Handle(store, state, Args(new { tm_op = "leftover" }));
            using var dryDoc = JsonDocument.Parse(JsonSerializer.Serialize(dry));
            Assert.True(dryDoc.RootElement.GetProperty("ok").GetBoolean());
            var mut = dryDoc.RootElement.GetProperty("mutation");
            Assert.True(mut.GetProperty("dry_run").GetBoolean());
            Assert.Equal(2, mut.GetProperty("count").GetInt32());
            Assert.Equal(focusId, state.ActiveStageId);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Apply_marks_done_without_stealing_focus()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-leftover-apply-" + Guid.NewGuid().ToString("N") + ".witdb");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "leftover-apply", null);

            var focusId = store.StageUpsert(state, "keep focus", null, null, null).stage_id;
            store.FocusStage(state, focusId);

            var readyId = store.StageUpsert(state, "close me", null, null, null).stage_id;
            store.StageSetStatus(state, readyId, "parked");
            var ac = store.StageCriterionAdd(state, readyId, "ac", "AC", null, null);
            var dod = store.StageCriterionAdd(state, readyId, "dod", "DoD", null, null);
            store.StageCriterionSetStatus(state, CritId(ac), "met");
            store.StageCriterionSetStatus(state, CritId(dod), "met");

            var applied = IdeTaskManager.Handle(store, state, Args(new { tm_op = "leftover", apply = true }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(applied));
            var mut = doc.RootElement.GetProperty("mutation");
            Assert.True(mut.GetProperty("apply").GetBoolean());
            Assert.Equal(1, mut.GetProperty("closed_count").GetInt32());
            Assert.True(mut.GetProperty("focus_preserved").GetBoolean());
            Assert.Equal(focusId, state.ActiveStageId);

            using var db = Open(path);
            var stage = db.Stages.Single(s => s.Id == readyId);
            Assert.Equal("done", stage.Status);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    static StageCriterionEntity Row(string kind, string status) => new()
    {
        Id = Guid.NewGuid(),
        StageId = Guid.NewGuid(),
        Kind = kind,
        Body = kind,
        Mode = "manual",
        Status = status,
        Ordinal = 0,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    static Guid CritId(object dto)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto));
        return doc.RootElement.GetProperty("criterion_id").GetGuid();
    }

    static Dictionary<string, JsonElement> Args(object anon)
    {
        var json = JsonSerializer.Serialize(anon);
        using var doc = JsonDocument.Parse(json);
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
        store.EnsureWorkFocusTable();
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
