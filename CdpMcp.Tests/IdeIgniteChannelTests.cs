using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteChannelTests
{
    [Fact]
    public void ToolName_is_cdp_ignite() =>
        Assert.Equal("cdp_ignite", IdeIgniteChannel.ToolName);

    [Fact]
    public void Schema_is_ignite_v0() =>
        Assert.Equal("ignite/v0", IdeIgniteChannel.Schema);

    [Fact]
    public void Send_without_message_returns_message_required()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("send"),
            ["port"] = JsonSerializer.SerializeToElement(1) // unused when message missing
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("message_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void IsProviderBlockedError_recognizes_canonical_code() =>
        Assert.True(IdeIgniteChannel.IsProviderBlockedError(IdeIgniteChannel.ProviderBlockedError));

    [Fact]
    public void SanitizeComposerCharge_scrubs_shell_tokens_for_provider()
    {
        var raw = "[autoignite/shell_finished] Ship shell_finished + async ensure";
        var clean = IdeIgniteChannel.SanitizeComposerCharge(raw);
        Assert.DoesNotContain("shell", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terminal_finished", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeArmFireCharge_includes_canonical_wake_and_amnesia_postfix()
    {
        var charge = IdeIgniteChannel.ComposeArmFireCharge();
        Assert.Contains(IdeIgniteChannel.CanonicalComposerCharge, charge, StringComparison.Ordinal);
        Assert.Contains("thread amnesia", charge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cdp_pressure op=recall", charge, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeRemountInitializedCharge_includes_initialized_lead()
    {
        var charge = IdeIgniteChannel.ComposeRemountInitializedCharge();
        Assert.StartsWith(IdeIgniteChannel.RemountInitializedLead, charge, StringComparison.Ordinal);
        Assert.Contains("Habitat=CDP", charge, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeOomWakeCharge_includes_oom_lead_and_recall()
    {
        var charge = IdeIgniteChannel.ComposeOomWakeCharge();
        Assert.StartsWith(IdeIgniteChannel.OomWakeLead, charge, StringComparison.Ordinal);
        Assert.Contains("cdp_pressure op=recall", charge, StringComparison.Ordinal);
        Assert.True(IdeIgniteChannel.LooksLikeAutoIgnitionCharge(charge));
    }

    [Fact]
    public void EventTokenForCharge_maps_shell_finished_event_id()
    {
        Assert.Equal("terminal_finished", IdeIgniteChannel.EventTokenForCharge("shell_finished"));
        Assert.Equal("build_finished", IdeIgniteChannel.EventTokenForCharge("build_finished"));
    }

    [Fact]
    public void Probe_unreachable_port_is_not_ok()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("scene"),
            ["port"] = JsonSerializer.SerializeToElement(1)
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ignite/v0", doc.RootElement.GetProperty("schema").GetString());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("error", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err.GetString()));
    }
}
