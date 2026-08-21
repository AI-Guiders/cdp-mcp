using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class CdpPeekChannelTests : IDisposable
{
    readonly string _root;
    readonly SessionContext _session = new();

    public CdpPeekChannelTests()
    {
        _root = Directory.CreateTempSubdirectory("cdp-peek-").FullName;
        _session.ProjectRoot = _root;
    }

    public void Dispose() => TryDelete(_root);

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Single_file_numbered_text_and_anchors()
    {
        var file = Path.Combine(_root, "a.cs");
        File.WriteAllText(file, "line1\nline2\nline3\n");

        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, null,
            new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement("a.cs"),
                ["limit"] = JsonSerializer.SerializeToElement(2)
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("file", root.GetProperty("mode").GetString());
        Assert.Equal(3, root.GetProperty("total_lines").GetInt32());
        Assert.Equal(2, root.GetProperty("returned").GetInt32());
        Assert.True(root.GetProperty("has_more").GetBoolean());
        Assert.Equal(3, root.GetProperty("next_offset").GetInt32());
        Assert.Contains("     1|line1", root.GetProperty("text").GetString());
        var anchor = root.GetProperty("lines")[0].GetProperty("anchor").GetString();
        Assert.Contains("a.cs", anchor);
        Assert.Contains("L:1", anchor);
    }

    [Fact]
    public void Negative_offset_from_eof()
    {
        var file = Path.Combine(_root, "tail.txt");
        File.WriteAllLines(file, Enumerable.Range(1, 10).Select(i => $"L{i}"));

        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, null,
            new Dictionary<string, JsonElement>
            {
                ["path"] = JsonSerializer.SerializeToElement("tail.txt"),
                ["offset"] = JsonSerializer.SerializeToElement(-2),
                ["limit"] = JsonSerializer.SerializeToElement(2)
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal(9, root.GetProperty("offset").GetInt32());
        Assert.Contains("     9|L9", root.GetProperty("text").GetString());
        Assert.Contains("    10|L10", root.GetProperty("text").GetString());
    }

    [Fact]
    public void Anchor_land_pad()
    {
        var file = Path.Combine(_root, "src", "x.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllLines(file, Enumerable.Range(1, 20).Select(i => $"code{i}"));

        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, null,
            new Dictionary<string, JsonElement>
            {
                ["anchor"] = JsonSerializer.SerializeToElement("[F:src/x.cs;L:10;]"),
                ["pad"] = JsonSerializer.SerializeToElement(1)
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal(9, root.GetProperty("offset").GetInt32());
        Assert.Equal(3, root.GetProperty("returned").GetInt32());
    }

    [Fact]
    public void Batch_paths_respects_cap()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "1\n");
        File.WriteAllText(Path.Combine(_root, "two.txt"), "2\n");

        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, null,
            new Dictionary<string, JsonElement>
            {
                ["paths"] = JsonSerializer.SerializeToElement(new[] { "one.txt", "two.txt" })
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("batch", root.GetProperty("mode").GetString());
        Assert.Equal(2, root.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Lazy_bind_sets_project_root()
    {
        var outer = Directory.CreateTempSubdirectory("cdp-peek-bind-");
        try
        {
            var proj = Path.Combine(outer.FullName, "proj");
            Directory.CreateDirectory(proj);
            File.WriteAllText(Path.Combine(proj, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var src = Path.Combine(proj, "Program.cs");
            File.WriteAllText(src, "class P {}\n");

            var session = new SessionContext();
            var json = CdpPeekChannel.HandleJson(session, LanguageRegistry.Default, null,
                new Dictionary<string, JsonElement>
                {
                    ["path"] = JsonSerializer.SerializeToElement(src),
                    ["bind"] = JsonSerializer.SerializeToElement(true)
                });

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.NotNull(session.ProjectRoot);
            Assert.Contains("proj", session.ProjectRoot!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(outer.FullName);
        }
    }

    [Fact]
    public void Binary_refused()
    {
        var file = Path.Combine(_root, "blob.bin");
        File.WriteAllBytes(file, new byte[] { 0, 1, 2, 0, 4 });

        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, null,
            new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("blob.bin") });

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("binary", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Meta_catalog_includes_cdp_peek()
    {
        var tool = MetaToolCatalog.Build().Single(t => t.Name == "cdp_peek");
        Assert.Contains("ADR-0201", tool.Description);
    }

    [Fact]
    public void Find_alternation_auto_regex()
    {
        var file = Path.Combine(_root, "src", "FindAlt.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "class Alpha {}\nclass Beta {}\n");

        var store = new DocumentBufferStore();
        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, store,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("Alpha|Beta"),
                ["glob"] = JsonSerializer.SerializeToElement("*.cs"),
                ["max"] = JsonSerializer.SerializeToElement(5)
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("find", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("regex").GetBoolean());
        Assert.True(root.GetProperty("count").GetInt32() >= 1, json);
    }

    [Fact]
    public void Find_bare_filename_glob_normalized()
    {
        var file = Path.Combine(_root, "nested", "Target.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "needle-in-nested-target\n");

        var store = new DocumentBufferStore();
        var json = CdpPeekChannel.HandleJson(_session, LanguageRegistry.Default, store,
            new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("needle-in-nested-target"),
                ["glob"] = JsonSerializer.SerializeToElement("Target.cs"),
                ["max"] = JsonSerializer.SerializeToElement(3)
            });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("**/Target.cs", root.GetProperty("glob").GetString());
        Assert.True(root.GetProperty("count").GetInt32() >= 1, json);
    }

    [Fact]
    public void Find_lazy_bind_from_path()
    {
        var outer = Directory.CreateTempSubdirectory("cdp-peek-find-bind-");
        try
        {
            var proj = Path.Combine(outer.FullName, "proj");
            Directory.CreateDirectory(proj);
            File.WriteAllText(Path.Combine(proj, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var src = Path.Combine(proj, "src", "Worker.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(src)!);
            File.WriteAllText(src, "class Worker { const string Token = \"lazy-find-token\"; }\n");

            var session = new SessionContext();
            var store = new DocumentBufferStore();
            var json = CdpPeekChannel.HandleJson(session, LanguageRegistry.Default, store,
                new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonSerializer.SerializeToElement("lazy-find-token"),
                    ["path"] = JsonSerializer.SerializeToElement(proj),
                    ["glob"] = JsonSerializer.SerializeToElement("*.cs"),
                    ["max"] = JsonSerializer.SerializeToElement(3)
                });

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean(), json);
            Assert.NotNull(session.ProjectRoot);
            Assert.Contains("lazy_bind", root.GetProperty("bind").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.True(root.GetProperty("count").GetInt32() >= 1, json);
        }
        finally
        {
            TryDelete(outer.FullName);
        }
    }
}
