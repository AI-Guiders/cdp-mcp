using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CideIntercomPresenceLatchTests : IDisposable
{
    readonly string _root;

    public CideIntercomPresenceLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-icm-pres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
        CideIntercomPresenceLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideIntercomPresenceLatch.RootOverrideForTests = null;
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void PublishSeat_writes_dual_map_and_skips_idle_partner_line()
    {
        var doc = CideIntercomPresenceLatch.PublishSeat("pf", "busy");
        Assert.NotNull(doc);
        Assert.True(File.Exists(CideIntercomPresenceLatch.LatchPath));
        Assert.Equal("busy", doc!.Pf!.State);
        Assert.Equal("@PF · busy", CideIntercomPresenceLatch.PartnerLine("pm", doc));
        Assert.Null(CideIntercomPresenceLatch.PartnerLine("pf", doc)); // pm idle/missing
    }

    [Fact]
    public void PublishSeat_merges_without_clobbering_other_seat()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy");
        var doc = CideIntercomPresenceLatch.PublishSeat("pm", "composing");
        Assert.NotNull(doc);
        Assert.Equal("busy", doc!.Pf!.State);
        Assert.Equal("composing", doc.Pm!.State);
        Assert.Equal("@PM · composing", CideIntercomPresenceLatch.PartnerLine("pf", doc));
        Assert.Equal("@PF · busy", CideIntercomPresenceLatch.PartnerLine("pm", doc));
    }

    [Fact]
    public void TryReadEffective_marks_stale_after_ttl()
    {
        var pub = CideIntercomPresenceLatch.PublishSeat("pf", "composing", ttlSeconds: 1);
        Assert.NotNull(pub);
        pub!.Pf!.StampedUtc = DateTimeOffset.UtcNow.AddSeconds(-5);
        // rewrite aged stamp
        Directory.CreateDirectory(CideIntercomPresenceLatch.StateRoot);
        File.WriteAllText(
            CideIntercomPresenceLatch.LatchPath,
            JsonSerializer.Serialize(pub, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            }));

        var eff = CideIntercomPresenceLatch.TryReadEffective();
        Assert.NotNull(eff);
        Assert.Equal("stale", eff!.Pf!.State);
        Assert.Equal("@PF · stale", CideIntercomPresenceLatch.PartnerLine("pm", eff));
    }

    [Fact]
    public void Channel_presence_roundtrip()
    {
        var json = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("presence"),
            ["seat"] = JsonSerializer.SerializeToElement("pf"),
            ["state"] = JsonSerializer.SerializeToElement("busy")
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("busy", doc.RootElement.GetProperty("presence").GetProperty("pf").GetProperty("state").GetString());
        Assert.Equal("@PF · busy", doc.RootElement.GetProperty("partner_for_glass").GetString());

        var sceneJson = IdeCideIntercomChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        using var scene = JsonDocument.Parse(sceneJson);
        // scene.partner_presence = agent (PF) view of partner PM — idle/missing → null
        Assert.Equal(JsonValueKind.Null, scene.RootElement.GetProperty("partner_presence").ValueKind);
        Assert.Equal("busy", scene.RootElement.GetProperty("presence").GetProperty("pf").GetProperty("state").GetString());
    }

    [Fact]
    public void Idle_clears_partner_line()
    {
        CideIntercomPresenceLatch.PublishSeat("pf", "busy");
        var idle = CideIntercomPresenceLatch.PublishSeat("pf", "idle");
        Assert.Null(CideIntercomPresenceLatch.PartnerLine("pm", idle));
    }

    [Fact]
    public void AfferentLine_formats_both_seats_including_idle()
    {
        Assert.NotNull(CideIntercomPresenceLatch.PublishSeat("pf", "busy"));
        Assert.NotNull(CideIntercomPresenceLatch.PublishSeat("pm", "idle"));
        Assert.Equal(
            "presence | @PF busy · @PM idle",
            CideIntercomPresenceLatch.AfferentLine());
    }

    [Fact]
    public void Build_inject_adds_intercom_presence_line()
    {
        Assert.NotNull(CideIntercomPresenceLatch.PublishSeat("pf", "busy"));
        Assert.NotNull(CideIntercomPresenceLatch.PublishSeat("pm", "idle"));

        var built = CitizenCompletions.Build(
            "ping",
            boardLines: ["P  plan · x"],
            inject: true,
            mode: CitizenTurnMode.Dialog,
            history: false);
        Assert.NotNull(built.AfferentPulse);
        Assert.Contains("presence | @PF busy · @PM idle", built.AfferentPulse!, StringComparison.Ordinal);
    }
}
