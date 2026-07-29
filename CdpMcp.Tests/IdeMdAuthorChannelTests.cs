#nullable enable
using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeMdAuthorChannelTests
{
    [Fact]
    public void Scene_ok()
    {
        var session = new SessionContext();
        var result = IdeMdAuthorChannel.Handle(session, new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("scene")
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("md_author/v0", doc.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public void Expand_prose_include_and_export()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-md-author-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var snippet = Path.Combine(root, "bit.md");
            File.WriteAllText(snippet, "HELLO_SNIPPET\n");
            var md = Path.Combine(root, "doc.md");
            File.WriteAllText(md, "# Title\n\n{{ INCLUDE: bit.md }}\n\nTail\n");

            var session = new SessionContext { ProjectRoot = root };
            var expand = IdeMdAuthorChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("expand"),
                ["path"] = JsonSerializer.SerializeToElement(md)
            });
            var expandJson = JsonSerializer.Serialize(expand);
            using var expandDoc = JsonDocument.Parse(expandJson);
            Assert.True(expandDoc.RootElement.GetProperty("ok").GetBoolean(), expandJson);
            Assert.Contains("HELLO_SNIPPET", expandDoc.RootElement.GetProperty("markdown").GetString());
            Assert.DoesNotContain("INCLUDE", expandDoc.RootElement.GetProperty("markdown").GetString()!,
                StringComparison.OrdinalIgnoreCase);

            var export = IdeMdAuthorChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("export"),
                ["path"] = JsonSerializer.SerializeToElement(md)
            });
            var exportJson = JsonSerializer.Serialize(export);
            using var exportDoc = JsonDocument.Parse(exportJson);
            Assert.True(exportDoc.RootElement.GetProperty("ok").GetBoolean(), exportJson);
            var outPath = exportDoc.RootElement.GetProperty("out_path").GetString();
            Assert.True(File.Exists(outPath), outPath);
            Assert.Contains("HELLO_SNIPPET", File.ReadAllText(outPath!));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Fence_scope_skips_prose_include()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-md-author-fence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "bit.md"), "IN\n");
            var md = Path.Combine(root, "doc.md");
            File.WriteAllText(md, "Before\n{{ INCLUDE: bit.md }}\n```mermaid\n{{ INCLUDE: bit.md }}\n```\n");

            var session = new SessionContext { ProjectRoot = root };
            var result = IdeMdAuthorChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("expand"),
                ["path"] = JsonSerializer.SerializeToElement(md),
                ["scope"] = JsonSerializer.SerializeToElement("fence")
            });
            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            var body = doc.RootElement.GetProperty("markdown").GetString()!;
            Assert.Contains("{{ INCLUDE: bit.md }}", body);
            Assert.Contains("IN", body);
            // one include left in prose, one expanded in fence
            Assert.Equal(1, body.Split("INCLUDE", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Check_missing_include_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-md-author-miss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var md = Path.Combine(root, "doc.md");
            File.WriteAllText(md, "{{ INCLUDE: nope.md }}\n");
            var session = new SessionContext { ProjectRoot = root };
            var result = IdeMdAuthorChannel.Handle(session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("check"),
                ["path"] = JsonSerializer.SerializeToElement(md)
            });
            var json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.True(doc.RootElement.GetProperty("error_count").GetInt32() >= 1, json);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
