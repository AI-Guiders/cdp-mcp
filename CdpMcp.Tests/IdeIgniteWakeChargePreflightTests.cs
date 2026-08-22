using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteWakeChargePreflightTests
{
    [Fact]
    public void Probe_unbound_workspace_is_full_tier()
    {
        IdeStageCycle.Unbind();
        try
        {
            var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
            Assert.Equal(IdeIgniteChannel.WakeChargeTier.Full, preflight.Tier);
            Assert.StartsWith("TM:", preflight.TmStatusLine, StringComparison.Ordinal);
            Assert.Contains("unbound", preflight.TmStatusLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeStageCycle.Unbind();
        }
    }

    [Fact]
    public void Probe_empty_features_is_full_tier()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            Bind(store, state);

            var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
            Assert.Equal(IdeIgniteChannel.WakeChargeTier.Full, preflight.Tier);
            Assert.Contains("empty", preflight.TmStatusLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Probe_focused_leaf_is_minimal_tier_with_status_line()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf2-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Forge pilot", null);
            var leaf = store.StageUpsert(state, "F1b Windows Setup", null, null, null).stage_id;
            state.ActiveStageId = leaf;
            Bind(store, state);

            var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
            Assert.Equal(IdeIgniteChannel.WakeChargeTier.Minimal, preflight.Tier);
            Assert.Contains("[>]", preflight.TmStatusLine, StringComparison.Ordinal);
            Assert.Contains("F1b Windows Setup", preflight.TmStatusLine, StringComparison.Ordinal);
            Assert.Contains("feature=Forge pilot", preflight.TmStatusLine, StringComparison.Ordinal);
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComposeArmFireCharge_minimal_omits_human_face_postfix()
    {
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf3-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Forge pilot", null);
            store.StageUpsert(state, "F1b leaf", null, null, null);
            Bind(store, state);

            var charge = IdeIgniteChannel.ComposeArmFireCharge();
            Assert.Contains("TM:", charge, StringComparison.Ordinal);
            Assert.Contains("твоё — твоё", charge, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Human-face axe", charge, StringComparison.Ordinal);
            Assert.DoesNotContain("World dig (research freedom", charge, StringComparison.Ordinal);
            Assert.Contains("Compaction/amnesia", charge, StringComparison.Ordinal);
        }
        finally
        {
            IdeStageCycle.Unbind();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComposeArmFireCharge_full_includes_extended_blocks()
    {
        IdeStageCycle.Unbind();
        var charge = IdeIgniteChannel.ComposeArmFireCharge(
            new IdeIgniteChannel.WakeChargePreflight(
                IdeIgniteChannel.WakeChargeTier.Full,
                "TM: empty — test"));
        Assert.Contains("Human-face axe", charge, StringComparison.Ordinal);
        Assert.Contains("World dig", charge, StringComparison.Ordinal);
        Assert.Contains("Domain stamp", charge, StringComparison.Ordinal);
        Assert.Contains("Body recall", charge, StringComparison.Ordinal);
    }

    static void Bind(IntentWorkspaceStore store, IntentWorkspaceState state) =>
        IdeStageCycle.Bind(store, () => state);

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
}
