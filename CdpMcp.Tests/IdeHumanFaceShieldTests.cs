#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IdeWaveStore")]
public sealed class IdeHumanFaceShieldTests
{
    [Fact]
    public void Cide_done_refuses_without_shot_evidence()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-hf-refuse-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "glass-feat", null);
            var leaf = store.StageUpsert(state, "glass-leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CIDE");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("human_face_cide_shot", doc.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Cide_done_allows_with_evidence_png()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-hf-ok-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        var png = Path.Combine(dir, "glass-verify.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "glass-feat", null);
            var leaf = store.StageUpsert(state, "glass-leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CIDE");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new
            {
                tm_op = "done",
                evidence = png
            }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Cide_done_refuses_shot_true_bool_alone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-hf-shotbool-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "glass-feat", null);
            var leaf = store.StageUpsert(state, "glass-leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CIDE");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", shot = true }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("human_face_cide_shot", doc.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Cdp_product_done_not_gated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-hf-cdp-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "cdp-feat", null);
            var leaf = store.StageUpsert(state, "cdp-leaf", null, null, null).stage_id;
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
