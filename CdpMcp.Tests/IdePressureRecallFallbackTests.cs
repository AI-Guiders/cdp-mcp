#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.CdpState;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdePressureRecallFallbackTests : IDisposable
{
    public void Dispose()
    {
        IdeIgniteWakeLatch.RootOverrideForTests = null;
        IdePressureChannel.SealedCourseOverrideForTests = null;
    }

    [Fact]
    public void Recall_falls_back_to_ignite_wake_latch_when_tenant_stash_empty()
    {
        var iso = $"D:\\tmp\\cdp-pressure-fallback-{Guid.NewGuid():N}";
        var latchRoot = Path.Combine(iso, "latch");
        Directory.CreateDirectory(latchRoot);
        CdpProfile.ApplyClientRoots([iso]);
        IdeIgniteWakeLatch.RootOverrideForTests = latchRoot;

        try
        {
            _ = IdeIgniteWakeLatch.Publish(
                "test-arm",
                "Resume TM.",
                IdeIgniteWakeLatch.ChannelHabitat,
                course: "## operator_priority (SEALED)\n1. Forge demo-ready\n2. ANPM rollout");

            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));

            Assert.False(recall.RootElement.GetProperty("empty").GetBoolean());
            Assert.Equal("ignite_wake_latch", recall.RootElement.GetProperty("recall_source").GetString());
            Assert.Contains("Forge demo-ready", recall.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal("ready", recall.RootElement.GetProperty("recall_gate").GetString());
            Assert.True(recall.RootElement.GetProperty("ssot_auto").GetBoolean());
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-fallback-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Recall_falls_back_to_peer_tenant_witdb_when_current_stash_empty()
    {
        var iso = $"D:\\tmp\\cdp-pressure-peer-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        var wsRoot = CdpProfile.StateRoot;
        var peerRoot = Path.Combine(wsRoot, "tenants", "bridge1", "peer");
        var currentRoot = Path.Combine(wsRoot, "tenants", "bridge1", "current");
        Directory.CreateDirectory(peerRoot);

        var stashJson = """
            {"schema":"pressure_channel/v1","body":"## operator_priority (SEALED)\n1. Peer witdb leaf","stash_utc":"2026-09-13T10:00:00.0000000Z"}
            """.Trim();
        _ = new CdpStateStore(peerRoot).SetLatchDoc(IdePressureChannel.StashDocId, stashJson);

        using var tenant = CdpProfile.EnterTenantStateRoot(currentRoot);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));

            Assert.False(recall.RootElement.GetProperty("empty").GetBoolean());
            Assert.Equal("peer_tenant_stash", recall.RootElement.GetProperty("recall_source").GetString());
            Assert.Contains("Peer witdb leaf", recall.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal("ready", recall.RootElement.GetProperty("recall_gate").GetString());
            Assert.True(recall.RootElement.GetProperty("ssot_auto").GetBoolean());
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-peer-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
            try { Directory.Delete(wsRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Recall_peer_tenant_reads_witdb_when_interop_file_removed()
    {
        var iso = $"D:\\tmp\\cdp-pressure-peer-file-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        var wsRoot = CdpProfile.StateRoot;
        var peerRoot = Path.Combine(wsRoot, "tenants", "bridge1", "peer");
        var currentRoot = Path.Combine(wsRoot, "tenants", "bridge1", "current");
        Directory.CreateDirectory(peerRoot);

        var stashJson = """
            {"schema":"pressure_channel/v1","body":"## operator_priority (SEALED)\n1. Peer witdb only","stash_utc":"2026-09-13T11:00:00.0000000Z"}
            """.Trim();
        _ = new CdpStateStore(peerRoot).SetLatchDoc(IdePressureChannel.StashDocId, stashJson);

        var interopFile = Path.Combine(peerRoot, IdeIgniteArmHost.Seat, "pressure-stash.json");
        Directory.CreateDirectory(Path.GetDirectoryName(interopFile)!);
        File.WriteAllText(interopFile, stashJson);
        File.Delete(interopFile);

        using var tenant = CdpProfile.EnterTenantStateRoot(currentRoot);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));

            Assert.Equal("peer_tenant_stash", recall.RootElement.GetProperty("recall_source").GetString());
            Assert.Contains("Peer witdb only", recall.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-peer-file-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
            try { Directory.Delete(wsRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Recall_falls_back_to_canonical_when_stash_and_latch_empty()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-canonical-{Guid.NewGuid():N}";
        var latchRoot = Path.Combine(iso, "latch");
        Directory.CreateDirectory(latchRoot);
        CdpProfile.ApplyClientRoots([iso]);
        IdeIgniteWakeLatch.RootOverrideForTests = latchRoot;

        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));

            Assert.False(recall.RootElement.GetProperty("empty").GetBoolean());
            Assert.Equal("canonical_sealed_course", recall.RootElement.GetProperty("recall_source").GetString());
            Assert.Contains("Platform SSOT", recall.RootElement.GetProperty("body").GetString(), StringComparison.Ordinal);
            Assert.Equal("ready", recall.RootElement.GetProperty("recall_gate").GetString());
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-canonical-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    static Dictionary<string, JsonElement> Dict(params string[] kv)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < kv.Length; i += 2)
            d[kv[i]] = JsonSerializer.SerializeToElement(kv[i + 1]);
        return d;
    }
}
