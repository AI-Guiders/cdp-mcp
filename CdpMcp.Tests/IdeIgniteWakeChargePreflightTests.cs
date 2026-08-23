using System.Text.Json;
using Cdp.Core;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
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
    public void Probe_focused_leaf_empty_pressure_upgrades_to_full()
    {
        var latchRoot = Path.Combine(Path.GetTempPath(), "cdp-wake-pf-latch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(latchRoot);
        IdeIgniteWakeLatch.RootOverrideForTests = latchRoot;
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
            Assert.Equal(IdeIgniteChannel.WakeChargeTier.Full, preflight.Tier);
            Assert.Contains("[>]", preflight.TmStatusLine, StringComparison.Ordinal);
            Assert.Contains("pressure=empty", preflight.TmStatusLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeIgniteWakeLatch.RootOverrideForTests = null;
            IdeStageCycle.Unbind();
            try { Directory.Delete(latchRoot, recursive: true); } catch { /* ignore */ }
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Probe_focused_leaf_with_wake_latch_course_stays_minimal()
    {
        var latchRoot = Path.Combine(Path.GetTempPath(), "cdp-wake-pf-latch2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(latchRoot);
        IdeIgniteWakeLatch.RootOverrideForTests = latchRoot;
        _ = IdeIgniteWakeLatch.Publish(
            "test-arm",
            "Resume TM.",
            IdeIgniteWakeLatch.ChannelHabitat,
            course: "## operator_priority (SEALED)\n1. Forge demo-ready");
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf-latch3-" + Guid.NewGuid().ToString("N") + ".db");
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
            Assert.DoesNotContain("pressure=empty", preflight.TmStatusLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeIgniteWakeLatch.RootOverrideForTests = null;
            IdeStageCycle.Unbind();
            try { Directory.Delete(latchRoot, recursive: true); } catch { /* ignore */ }
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Probe_focused_leaf_with_hot_stash_is_minimal()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-wake-pressure-{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf-stash-" + Guid.NewGuid().ToString("N") + ".db");
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.Handle(session, Dict("op", "stash", "body", "## operator_priority\nForge F1b"));

            var store = BootStore(path);
            var state = new IntentWorkspaceState { DatabasePath = path };
            store.IntentUpsert(state, "Forge pilot", null);
            var leaf = store.StageUpsert(state, "F1b Windows Setup", null, null, null).stage_id;
            state.ActiveStageId = leaf;
            Bind(store, state);

            Assert.True(IdePressureChannel.HasHotStashBody());

            var preflight = IdeIgniteChannel.WakeChargePreflight.Probe();
            Assert.Equal(IdeIgniteChannel.WakeChargeTier.Minimal, preflight.Tier);
            Assert.Contains("[>]", preflight.TmStatusLine, StringComparison.Ordinal);
            Assert.DoesNotContain("pressure=empty", preflight.TmStatusLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            IdeStageCycle.Unbind();
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-wake-pressure-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* ignore */ }
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComposeArmFireCharge_minimal_omits_human_face_postfix()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-wake-min-{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), "cdp-wake-pf3-" + Guid.NewGuid().ToString("N") + ".db");
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.Handle(session, Dict("op", "stash", "body", "## Next\nF1b leaf"));

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
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-wake-min-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* ignore */ }
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ComposeArmFireCharge_full_includes_extended_blocks()
    {
        IdePressureChannel.SealedCourseOverrideForTests =
            "## operator_priority (SEALED)\n1. Glass Done\n2. Citizen Done";
        try
        {
            var charge = IdeIgniteChannel.ComposeArmFireCharge(
                new IdeIgniteChannel.WakeChargePreflight(
                    IdeIgniteChannel.WakeChargeTier.Full,
                    "TM: empty — test"));
            Assert.Contains("Human-face axe", charge, StringComparison.Ordinal);
            Assert.Contains("World dig", charge, StringComparison.Ordinal);
            Assert.Contains("Domain stamp", charge, StringComparison.Ordinal);
            Assert.Contains("Body recall", charge, StringComparison.Ordinal);
        }
        finally
        {
            IdePressureChannel.SealedCourseOverrideForTests = null;
        }
    }

    [Fact]
    public void ComposeArmFireCharge_full_omits_human_face_when_glass_deferred()
    {
        IdePressureChannel.SealedCourseOverrideForTests = IdePressureChannel.CanonicalSealedCourse;
        try
        {
            var charge = IdeIgniteChannel.ComposeArmFireCharge(
                new IdeIgniteChannel.WakeChargePreflight(
                    IdeIgniteChannel.WakeChargeTier.Full,
                    "TM: empty — test"));
            Assert.DoesNotContain("Human-face axe", charge, StringComparison.Ordinal);
            Assert.Contains("World dig", charge, StringComparison.Ordinal);
            Assert.Contains("Domain stamp", charge, StringComparison.Ordinal);
        }
        finally
        {
            IdePressureChannel.SealedCourseOverrideForTests = null;
        }
    }

    static Dictionary<string, JsonElement> Dict(params string[] kv)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < kv.Length; i += 2)
            d[kv[i]] = JsonSerializer.SerializeToElement(kv[i + 1]);
        return d;
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
