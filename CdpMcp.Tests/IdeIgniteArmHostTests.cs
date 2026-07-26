using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteArmHostTests
{
    [Fact]
    public void NormalizeEvent_maps_aliases()
    {
        Assert.Equal("build_finished", IdeIgniteArmHost.NormalizeEvent("build"));
        Assert.Equal("test_finished", IdeIgniteArmHost.NormalizeEvent("tests"));
        Assert.Equal("timer", IdeIgniteArmHost.NormalizeEvent("delay"));
        Assert.Equal("timer", IdeIgniteArmHost.NormalizeEvent("timer"));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    public void TryParseDuration_ok(string raw, int seconds)
    {
        Assert.True(IdeIgniteArmHost.TryParseDuration(raw, out var span));
        Assert.Equal(seconds, (int)span.TotalSeconds);
    }

    [Fact]
    public void Arm_timer_without_cdt_persists()
    {
        var id = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("unit-test next"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("arm", doc.RootElement.GetProperty("op").GetString());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        var listJson = JsonSerializer.Serialize(list);
        Assert.Contains(id, listJson, StringComparison.Ordinal);

        var disarm = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["id"] = JsonSerializer.SerializeToElement(id)
        });
        using var ddoc = JsonDocument.Parse(JsonSerializer.Serialize(disarm));
        Assert.True(ddoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, ddoc.RootElement.GetProperty("removed").GetInt32());
    }

    [Fact]
    public void Arm_requires_message_or_task()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1m")
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("message_or_task_required", doc.RootElement.GetProperty("error").GetString());
    }
}
