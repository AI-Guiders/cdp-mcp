#nullable enable

using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeHealthSnapshotTests
{
    [Fact]
    public void IdeIdeHealthChannel_scene_returns_segments()
    {
        var session = new SessionContext { ProjectRoot = Directory.GetCurrentDirectory() };
        var json = IdeIdeHealthChannel.HandleJson(session);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ide_health/v1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.GetProperty("segment_count").GetInt32() >= 4);
        var segments = doc.RootElement.GetProperty("segments");
        Assert.Equal(JsonValueKind.Array, segments.ValueKind);
        Assert.True(segments.GetArrayLength() >= 4);
    }

    [Fact]
    public void IdeIdeHealthChannel_pulse_has_summary()
    {
        var session = new SessionContext { ProjectRoot = Directory.GetCurrentDirectory() };
        var result = IdeIdeHealthChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("pulse"),
        });
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("summary", json, StringComparison.Ordinal);
    }
}
