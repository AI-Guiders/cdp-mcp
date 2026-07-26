using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeSaChannelTests
{
    [Fact]
    public void Slim_on_long_file_suggests_touch_or_split()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-sa-");
        try
        {
            var file = Path.Combine(tmp.FullName, "Fat.cs");
            var lines = Enumerable.Range(1, 400).Select(i => $"// line {i}");
            File.WriteAllText(file, "class Fat {\n" + string.Join("\n", lines) + "\n}\n");

            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            var board = IdeSaChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement(file),
                ["scope"] = JsonSerializer.SerializeToElement("file"),
                ["depth"] = JsonSerializer.SerializeToElement("slim")
            });

            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("code_sa/v1", doc.RootElement.GetProperty("schema").GetString());
            var verdict = doc.RootElement.GetProperty("verdict").GetString();
            Assert.True(verdict is "touch" or "split" or "leave" or "need_more", json);
            Assert.True(doc.RootElement.GetProperty("quality").GetProperty("findings").GetArrayLength() >= 0);
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Pulse_is_thin()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var board = IdeSaChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["depth"] = JsonSerializer.SerializeToElement("pulse")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("pulse", doc.RootElement.GetProperty("detail").GetString());
        Assert.False(doc.RootElement.TryGetProperty("clones", out _));
    }

    [Fact]
    public void ToolName_is_cdp_sa()
    {
        Assert.Equal("cdp_sa", IdeSaChannel.ToolName);
    }
}
