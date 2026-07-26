using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeFilesChannelTests
{
    [Fact]
    public void ToolName_is_cdp_files() =>
        Assert.Equal("cdp_files", IdeFilesChannel.ToolName);

    [Fact]
    public void Scene_lists_project_or_cwd()
    {
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var store = new DocumentBufferStore();
        var result = IdeFilesChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("scene"),
            ["where"] = JsonSerializer.SerializeToElement("project")
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("files/v1", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.TryGetProperty("cwd", out _));
        Assert.True(doc.RootElement.TryGetProperty("entries", out _));
    }

    [Fact]
    public void External_cd_does_not_require_project()
    {
        var session = new SessionContext();
        var store = new DocumentBufferStore();
        var temp = Path.Combine(Path.GetTempPath(), "cdp-fm-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            var result = IdeFilesChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("cd"),
                ["where"] = JsonSerializer.SerializeToElement("external"),
                ["path"] = JsonSerializer.SerializeToElement(temp)
            });
            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("external", doc.RootElement.GetProperty("where").GetString());
            Assert.Equal(Path.GetFullPath(temp), doc.RootElement.GetProperty("cwd").GetString());
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
            IdeSettingsStore.Unset(IdeFilesChannel.CwdKey);
        }
    }
}
