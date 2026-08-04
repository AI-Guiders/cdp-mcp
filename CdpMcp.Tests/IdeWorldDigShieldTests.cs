#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IdeWaveStore")]
public sealed class IdeWorldDigShieldTests
{
    [Fact]
    public void Invent_mill_done_refuses_without_dig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wd-refuse-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "feat", null);
            var leaf = store.StageUpsert(state, "SoftFL invent theater leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CDP");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("world_dig_missing", doc.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Invent_mill_done_allows_with_dig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wd-ok-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "feat", null);
            var leaf = store.StageUpsert(state, "board-hygiene inventory mill", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CDP");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "done",
                dig = "knowledge/worlds/hci-ux-dx/playbook-hci-core-v1.md"
            }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Normal_leaf_done_not_gated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wd-norm-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "feat", null);
            var leaf = store.StageUpsert(state, "Ship FDS card deck", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CDP");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Arm_charge_includes_world_dig_postfix()
    {
        var charge = IdeIgniteChannel.ComposeArmFireCharge();
        Assert.Contains("World dig", charge, StringComparison.Ordinal);
        Assert.Contains("Training memory", charge, StringComparison.Ordinal);
        Assert.Contains("Human-face axe", charge, StringComparison.Ordinal);
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
        store.EnsureStageProductColumn();
        return store;
    }
}
