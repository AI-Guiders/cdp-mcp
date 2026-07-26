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
