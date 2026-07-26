using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class FindInFilesTests
{
    [Theory]
    [InlineData("project", true, false)]
    [InlineData("files", true, false)]
    [InlineData("external", true, true)]
    [InlineData("disk", true, true)]
    [InlineData("system", true, true)]
    [InlineData("buffer", false, false)]
    [InlineData(null, false, false)]
    public void Scope_classification(string? scope, bool files, bool external)
    {
        Assert.Equal(files, FindInFiles.IsFilesScope(scope));
        Assert.Equal(external, FindInFiles.IsExternalScope(scope));
    }

    [Fact]
    public void External_requires_path()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext();
        var json = FindInFiles.Dispatch(
            store,
            session,
            new Dictionary<string, JsonElement>
            {
                ["scope"] = JsonSerializer.SerializeToElement("external"),
                ["query"] = JsonSerializer.SerializeToElement("needle")
            },
            all: false);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("external", doc.RootElement.GetProperty("scope").GetString());
        Assert.Equal("path_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void External_searches_outside_project_without_open()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-find-ext-");
        try
        {
            var file = Path.Combine(tmp.FullName, "hit.txt");
            File.WriteAllText(file, "unique-external-needle-42\n");

            var store = new DocumentBufferStore();
            var session = new SessionContext(); // no ProjectRoot
            var json = FindInFiles.Dispatch(
                store,
                session,
                new Dictionary<string, JsonElement>
                {
                    ["scope"] = JsonSerializer.SerializeToElement("external"),
                    ["path"] = JsonSerializer.SerializeToElement(tmp.FullName),
                    ["query"] = JsonSerializer.SerializeToElement("unique-external-needle-42"),
                    ["peek"] = JsonSerializer.SerializeToElement(false),
                    ["max"] = JsonSerializer.SerializeToElement(5)
                },
                all: true);

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("external", doc.RootElement.GetProperty("scope").GetString());
            Assert.True(doc.RootElement.GetProperty("count").GetInt32() >= 1);
            var hitPath = doc.RootElement.GetProperty("hits")[0].GetProperty("path").GetString();
            Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(hitPath!));
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
