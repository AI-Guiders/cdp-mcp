#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdePressureExplainTests
{
    [Fact]
    public void Scene_and_pulse_expose_stashed_explain_card()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-explain-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Act, Object = CdpObjectKind.Code };
            _ = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["why"] = JsonSerializer.SerializeToElement("unit test L1")
            });
            _ = IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stash"),
                ["body"] = JsonSerializer.SerializeToElement("## Pressure stash\n\n### AutoIgnition\nok")
            });

            using var scene = JsonDocument.Parse(IdePressureChannel.HandleJson(session));
            var explain = scene.RootElement.GetProperty("explain");
            Assert.Equal("pressure.continuity", explain.GetProperty("source").GetString());
            Assert.Equal("stashed", explain.GetProperty("reason").GetString());
            Assert.Equal("cdp_pressure op=recall", explain.GetProperty("next_step").GetString());

            var pulse = IdePressureChannel.PulseCardOrNull();
            Assert.NotNull(pulse);
            using var pulseDoc = JsonDocument.Parse(JsonSerializer.Serialize(pulse));
            Assert.Equal("stashed", pulseDoc.RootElement.GetProperty("explain").GetProperty("reason").GetString());
            Assert.Contains("pressure.continuity", IdePressureChannel.ExplainWhyLine(), StringComparison.Ordinal);
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-explain-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Need_stash_explain_when_armed_without_body()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var iso = $"D:\\tmp\\cdp-pressure-needstash-{Guid.NewGuid():N}";
        CdpProfile.ApplyClientRoots([iso]);
        try
        {
            var session = new SessionContext { ProjectRoot = iso, Phase = CdpPhase.Act, Object = CdpObjectKind.Code };
            using var arm = JsonDocument.Parse(JsonSerializer.Serialize(
                IdePressureChannel.Handle(session, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["op"] = JsonSerializer.SerializeToElement("arm")
                })));
            Assert.Equal("need_stash", arm.RootElement.GetProperty("explain").GetProperty("reason").GetString());
            Assert.Equal("cdp_pressure op=stash body=", arm.RootElement.GetProperty("explain").GetProperty("next_step").GetString());
        }
        finally
        {
            CdpProfile.ApplyClientRoots(["D:\\tmp\\cdp-pressure-needstash-cleanup"]);
            try { Directory.Delete(iso, recursive: true); } catch { /* best-effort */ }
        }
    }
}
