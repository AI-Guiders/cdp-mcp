#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeToolchainChannelTests
{
    [Fact]
    public void Scene_lists_builtin_ids()
    {
        var session = new SessionContext();
        var result = IdeToolchainChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("dal", doc.RootElement.GetProperty("seam").GetString());
        var rows = doc.RootElement.GetProperty("toolchains");
        Assert.True(rows.GetArrayLength() >= 4, json);
        var ids = rows.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("python", ids);
        Assert.Contains("gcc", ids);
        Assert.Contains("javac", ids);
        Assert.Contains("go", ids);
    }

    [Fact]
    public void Ensure_no_recipe_returns_search_next()
    {
        var session = new SessionContext();
        var result = IdeToolchainChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("ensure"),
            ["id"] = JsonSerializer.SerializeToElement("definitely-missing-toolchain-xyz")
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("no_recipe", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void Ensure_already_ok_when_python_on_path()
    {
        // Skip if python not installed — still validates probe path.
        var session = new SessionContext();
        var probe = IdeToolchainChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("probe"),
            ["id"] = JsonSerializer.SerializeToElement("python")
        });
        var probeJson = JsonSerializer.Serialize(probe);
        using var probeDoc = JsonDocument.Parse(probeJson);
        var row = probeDoc.RootElement.GetProperty("toolchains")[0];
        if (!row.GetProperty("ok").GetBoolean())
            return; // environment without python

        var ensure = IdeToolchainChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("ensure"),
            ["id"] = JsonSerializer.SerializeToElement("python")
        });
        var json = JsonSerializer.Serialize(ensure);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("already_ok", doc.RootElement.GetProperty("status").GetString());
    }
}
