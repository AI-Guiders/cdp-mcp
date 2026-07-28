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

    [Fact]
    public void Text_dumps_plain_file_with_cap()
    {
        var session = new SessionContext();
        var store = new DocumentBufferStore();
        var temp = Path.Combine(Path.GetTempPath(), "cdp-fm-text-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        var file = Path.Combine(temp, "note.txt");
        File.WriteAllText(file, new string('a', 2000) + "TAIL");
        try
        {
            var result = IdeFilesChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("text"),
                ["path"] = JsonSerializer.SerializeToElement(file),
                ["max_chars"] = JsonSerializer.SerializeToElement(500)
            });
            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("text", doc.RootElement.GetProperty("op").GetString());
            Assert.Equal("utf8", doc.RootElement.GetProperty("engine").GetString());
            Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
            Assert.Equal(500, doc.RootElement.GetProperty("chars").GetInt32());
            Assert.Equal(500, doc.RootElement.GetProperty("text").GetString()!.Length);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
            IdeSettingsStore.Unset(IdeFilesChannel.CwdKey);
        }
    }

    [Fact]
    public void Open_routes_html_to_text_when_pandoc_available()
    {
        if (CdpMcp.Cockpit.DataAcquisition.ToolchainPathProbe.Resolve("pandoc") is null)
            return; // environment without pandoc

        var session = new SessionContext();
        var store = new DocumentBufferStore();
        var temp = Path.Combine(Path.GetTempPath(), "cdp-fm-html-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        var file = Path.Combine(temp, "page.html");
        File.WriteAllText(file, "<html><body><h1>Hello Doc</h1><p>Body line.</p></body></html>");
        try
        {
            var result = IdeFilesChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("open"),
                ["path"] = JsonSerializer.SerializeToElement(file)
            });
            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("text", doc.RootElement.GetProperty("op").GetString());
            Assert.Equal("pandoc", doc.RootElement.GetProperty("engine").GetString());
            var text = doc.RootElement.GetProperty("text").GetString() ?? "";
            Assert.Contains("Hello Doc", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
            IdeSettingsStore.Unset(IdeFilesChannel.CwdKey);
        }
    }
}
