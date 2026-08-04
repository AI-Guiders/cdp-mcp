#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IdeWaveStore")]
public sealed class IdeFeatureDoneAxbTests
{
    [Fact]
    public void Feature_done_refuses_half_a_when_autonomous_and_no_wave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-axb-refuse-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var wavePath = Path.Combine(dir, "active-wave.json");
        var dbPath = Path.Combine(dir, "tm.witdb");
        IdeWaveChannel.FilePathOverride = () => wavePath;
        IdeIgniteArmHost.BindAutonomous(true);
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "axb-feat", null);
            var leaf = store.StageUpsert(state, "leaf", null, null, null).stage_id;
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "axb-feat" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("a\u00d7b half-a", doc.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(state.ActiveIntentId);
        }
        finally
        {
            IdeIgniteArmHost.BindAutonomous(null);
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Feature_done_allows_when_wave_active()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-axb-wave-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var wavePath = Path.Combine(dir, "active-wave.json");
        var dbPath = Path.Combine(dir, "tm.witdb");
        IdeWaveChannel.FilePathOverride = () => wavePath;
        IdeIgniteArmHost.BindAutonomous(true);
        try
        {
            IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("seed"),
                ["title"] = JsonSerializer.SerializeToElement("AxbAllow"),
                ["items"] = JsonSerializer.SerializeToElement("a;b")
            });
            IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("start")
            });

            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "axb-wave-feat", null);
            var leaf = store.StageUpsert(state, "leaf", null, null, null).stage_id;
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "axb-wave-feat" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("feature_done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
            Assert.Null(state.ActiveIntentId);
        }
        finally
        {
            IdeIgniteArmHost.BindAutonomous(null);
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Feature_done_force_escapes_half_a_refuse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-axb-force-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var wavePath = Path.Combine(dir, "active-wave.json");
        var dbPath = Path.Combine(dir, "tm.witdb");
        IdeWaveChannel.FilePathOverride = () => wavePath;
        IdeIgniteArmHost.BindAutonomous(true);
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "axb-force-feat", null);
            var leaf = store.StageUpsert(state, "leaf", null, null, null).stage_id;
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "axb-force-feat", force = true }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("feature_done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
        }
        finally
        {
            IdeIgniteArmHost.BindAutonomous(null);
            IdeWaveChannel.FilePathOverride = null;
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
        return store;
    }
}
