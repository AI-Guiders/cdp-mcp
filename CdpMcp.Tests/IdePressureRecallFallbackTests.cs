#nullable enable
using System.Text.Json;
using Cdp.Core;
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
