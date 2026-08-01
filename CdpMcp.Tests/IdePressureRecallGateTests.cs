#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CdpProfileIsolation")]
public sealed class IdePressureRecallGateTests
{
    [Fact]
    public void Recall_enters_pull_then_reconcile_align_ready()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-gate-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.Handle(session, Dict("op", "arm"));
            _ = IdePressureChannel.Handle(session, Dict("op", "stash", "body", "## Domain\nGlass primary"));

            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));
            Assert.Equal("pull", recall.RootElement.GetProperty("recall_gate").GetString());
            Assert.False(recall.RootElement.GetProperty("ssot_auto").GetBoolean());
            Assert.Contains("recall·pull", IdePressureChannel.PulseLine(), StringComparison.Ordinal);
            Assert.Equal("recall_pull", recall.RootElement.GetProperty("explain").GetProperty("reason").GetString());

            using var recon = JsonDocument.Parse(IdePressureChannel.HandleJson(
                session, Dict("op", "reconcile", "note", "self-steer Glass")));
            Assert.Equal("reconcile", recon.RootElement.GetProperty("recall_gate").GetString());
            Assert.Equal("recall_reconcile", recon.RootElement.GetProperty("explain").GetProperty("reason").GetString());

            using var align = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "align")));
            Assert.Equal("align", align.RootElement.GetProperty("recall_gate").GetString());

            using var ready = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "ready")));
            Assert.Equal("ready", ready.RootElement.GetProperty("recall_gate").GetString());
            Assert.Equal("recall_ready", ready.RootElement.GetProperty("explain").GetProperty("reason").GetString());
            Assert.Contains("recall·ready", IdePressureChannel.PulseLine(), StringComparison.Ordinal);
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-gate-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Recall_auto_ready_when_ssot_sufficient()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-ssot-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Recall, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.Handle(session, Dict("op", "arm"));
            _ = IdePressureChannel.Handle(session, Dict(
                "op", "stash",
                "body", "## Domain\nADX primary · pressure ceremony tax cut · habitat=CDP\n## Next\nship auto-ready",
                "plan", "ADX pressure ssot_auto",
                "ignite", "last_once 3m"));

            using var recall = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "recall")));
            Assert.Equal("ready", recall.RootElement.GetProperty("recall_gate").GetString());
            Assert.True(recall.RootElement.GetProperty("ssot_auto").GetBoolean());
            Assert.Equal("recall_ready", recall.RootElement.GetProperty("explain").GetProperty("reason").GetString());
            Assert.Contains("recall·ready", IdePressureChannel.PulseLine(), StringComparison.Ordinal);

            using var strict = JsonDocument.Parse(IdePressureChannel.HandleJson(
                session, DictBool("op", "recall", "strict", true)));
            Assert.Equal("pull", strict.RootElement.GetProperty("recall_gate").GetString());
            Assert.False(strict.RootElement.GetProperty("ssot_auto").GetBoolean());

            using var steer = JsonDocument.Parse(IdePressureChannel.HandleJson(session, Dict("op", "steer")));
            Assert.Equal("ready", steer.RootElement.GetProperty("recall_gate").GetString());
            Assert.True(steer.RootElement.GetProperty("ssot_auto").GetBoolean());
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-ssot-cleanup"]);
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

    static Dictionary<string, JsonElement> DictBool(string opKey, string opVal, string boolKey, bool boolVal)
    {
        var d = Dict(opKey, opVal);
        d[boolKey] = JsonSerializer.SerializeToElement(boolVal);
        return d;
    }
}
