#nullable enable
using System.Text.Json;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IdeWaveStore")]
public sealed class IdeDomainStampShieldTests
{
    [Fact]
    public void Cide_done_refuses_without_domain_arg()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-ds-refuse-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        var png = Path.Combine(dir, "shot.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
        try
        {
            var store = BootStore(dbPath);
            var state = new IntentWorkspaceState { DatabasePath = dbPath };
            store.IntentUpsert(state, "glass-feat", null);
            var leaf = store.StageUpsert(state, "glass-leaf", null, null, null).stage_id;
            store.StageSetProduct(state, leaf, "CIDE");
            store.FocusStage(state, leaf);

            var result = IdeTaskManager.Handle(store, state, Args(new { tm_op = "done", evidence = png }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("domain_stamp_missing", doc.RootElement.GetProperty("error").GetString()!, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Cide_done_allows_with_fresh_domain_stamp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-ds-ok-" + Guid.NewGuid().ToString("n"));
        var domainDir = Path.Combine(dir, ".cdp", "domain");
        Directory.CreateDirectory(domainDir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        var png = Path.Combine(dir, "shot.png");
        File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(domainDir, "glass.md"),
            $"""
            # Domain card: glass
            - id: `glass`
            ## Invariants
            - test
            ## Entry
            - test
            ## Antipatterns
            - test
            ## last_ship
            - **{today}** anti-rooster stamp shield dogfood
            """);
        var prev = IdeDomainPulse.DirOverrideForTests;
        IdeDomainPulse.DirOverrideForTests = domainDir;
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
                evidence = png,
                domain = "glass"
            }));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("done", doc.RootElement.GetProperty("mutation").GetProperty("op").GetString());
        }
        finally
        {
            IdeDomainPulse.DirOverrideForTests = prev;
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Cide_done_force_escapes_stamp_shield()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-ds-force-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "tm.witdb");
        var png = Path.Combine(dir, "shot.png");
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
                evidence = png,
                force = true
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
    public void Stamp_pending_mark_clear_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-ds-pending-" + Guid.NewGuid().ToString("n") + ".json");
        var prev = IdeDomainStampPending.PathOverrideForTests;
        IdeDomainStampPending.PathOverrideForTests = path;
        try
        {
            IdeDomainStampPending.Clear();
            Assert.False(IdeDomainStampPending.IsSet());
            IdeDomainStampPending.Mark("test");
            Assert.True(IdeDomainStampPending.IsSet());
            IdeDomainStampPending.Clear();
            Assert.False(IdeDomainStampPending.IsSet());
        }
        finally
        {
            IdeDomainStampPending.PathOverrideForTests = prev;
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ComposeArmFireCharge_includes_domain_stamp_postfix()
    {
        var charge = IdeIgniteChannel.ComposeArmFireCharge();
        Assert.Contains(IdeIgniteChannel.ChargeDomainStampPostfix.Trim(), charge, StringComparison.Ordinal);
        Assert.Contains("anti-rooster", charge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SAME turn", charge, StringComparison.Ordinal);
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
