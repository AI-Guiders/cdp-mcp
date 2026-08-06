#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IdeWaveStore")]
public sealed class IdeWaveShipShieldTests
{
    [Fact]
    public void Cdp_wave_ships_without_evidence_when_all_items_done()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wv-cdp-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            SeedWave("Throughput", "a;b;c");
            MarkDone("a");
            MarkDone("b");
            MarkDone("c");

            var shipped = JsonSerializer.Serialize(Ship());
            Assert.Contains("\"ok\":true", shipped.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shipped", shipped, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Wave_shipped_refuses_pending_items()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wv-pend-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            SeedWave("Throughput", "a;b;c");
            MarkDone("a");

            var json = JsonSerializer.Serialize(Ship());
            Assert.Contains("wave_ship_pending_items", json, StringComparison.Ordinal);
            Assert.Contains("\"ok\":false", json.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Glass_wave_shipped_refuses_without_png()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wv-glass-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "active-wave.json");
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            SeedWave("share-glass-axb", "mirror share;FDS chip");
            MarkDone("mirror share");
            MarkDone("FDS chip");

            var json = JsonSerializer.Serialize(Ship());
            Assert.Contains("human_face_cide_shot", json, StringComparison.Ordinal);
            Assert.Contains("\"ok\":false", json.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Glass_wave_shipped_allows_with_evidence_and_domain_stamp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wv-glass-ok-" + Guid.NewGuid().ToString("n"));
        var domainDir = Path.Combine(dir, ".cdp", "domain");
        Directory.CreateDirectory(domainDir);
        var path = Path.Combine(dir, "active-wave.json");
        var png = Path.Combine(dir, "glass-wave.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(domainDir, "glass.md"),
            $"""
            # glass
            - id: `glass`
            ## Invariants
            - x
            ## Entry
            - x
            ## Antipatterns
            - x
            ## last_ship
            - {today} wave ship shield dogfood
            """);
        var prev = IdeDomainPulse.DirOverrideForTests;
        IdeDomainPulse.DirOverrideForTests = domainDir;
        IdeWaveChannel.FilePathOverride = () => path;
        try
        {
            SeedWave("share-glass-axb", "mirror;FDS");
            MarkDone("mirror");
            MarkDone("FDS");

            var shipped = JsonSerializer.Serialize(Ship(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["evidence"] = JsonSerializer.SerializeToElement(png),
                ["domain"] = JsonSerializer.SerializeToElement("glass"),
                ["project_root"] = JsonSerializer.SerializeToElement(dir)
            }));
            Assert.Contains("\"ok\":true", shipped.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shipped", shipped, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeDomainPulse.DirOverrideForTests = prev;
            IdeWaveChannel.FilePathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Feature_done_cide_refuses_without_teeth()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-wv-feat-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "Glass peel wave", null);
            var leaf = store.StageUpsert(state, "leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CIDE");
            store.FocusStage(state, leaf);
            state.ActiveStageId = null;

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", title = "Glass peel wave" }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("human_face_cide_shot", doc.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    static void SeedWave(string title, string items) =>
        IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("seed"),
            ["title"] = JsonSerializer.SerializeToElement(title),
            ["items"] = JsonSerializer.SerializeToElement(items)
        });

    static void MarkDone(string label) =>
        IdeWaveChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("item_done"),
            ["label"] = JsonSerializer.SerializeToElement(label)
        });

    static object Ship(IReadOnlyDictionary<string, JsonElement>? extra = null)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("shipped")
        };
        if (extra is not null)
        {
            foreach (var kv in extra)
                args[kv.Key] = kv.Value;
        }

        return IdeWaveChannel.Handle(args);
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
