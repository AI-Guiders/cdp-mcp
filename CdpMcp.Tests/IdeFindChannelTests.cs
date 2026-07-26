using System.Text.Json;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public class IdeFindChannelTests
{
    [Fact]
    public void Query_required()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext { ProjectRoot = Path.GetTempPath() };
        var board = IdeFindChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["where"] = JsonSerializer.SerializeToElement("project")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("query_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void What_index_is_stub()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext();
        var board = IdeFindChannel.Handle(store, session, new Dictionary<string, JsonElement>
        {
            ["what"] = JsonSerializer.SerializeToElement("index"),
            ["query"] = JsonSerializer.SerializeToElement("anything")
        });

        var json = JsonSerializer.Serialize(board);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("what_index_deferred", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Project_slim_finds_and_saves_last()
    {
        var tmp = Directory.CreateTempSubdirectory("cdp-find-organ-");
        try
        {
            var file = Path.Combine(tmp.FullName, "hit.txt");
            File.WriteAllText(file, "unique-organ-needle-99\n");

            IdeSettingsStore.Unset(IdeFindChannel.LastKey);

            var store = new DocumentBufferStore();
            var session = new SessionContext { ProjectRoot = tmp.FullName };
            var board = IdeFindChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["query"] = JsonSerializer.SerializeToElement("unique-organ-needle-99"),
                ["where"] = JsonSerializer.SerializeToElement("project"),
                ["shape"] = JsonSerializer.SerializeToElement("slim"),
                ["peek"] = JsonSerializer.SerializeToElement(false),
                ["max"] = JsonSerializer.SerializeToElement(5)
            });

            var json = JsonSerializer.Serialize(board);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.Equal("slim", doc.RootElement.GetProperty("shape").GetString());
            Assert.True(doc.RootElement.GetProperty("count").GetInt32() >= 1);

            var last = IdeFindChannel.Handle(store, session, new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("last")
            });
            var lastJson = JsonSerializer.Serialize(last);
            using var lastDoc = JsonDocument.Parse(lastJson);
            Assert.True(lastDoc.RootElement.GetProperty("ok").GetBoolean(), lastJson);
            Assert.False(lastDoc.RootElement.GetProperty("idle").GetBoolean());
        }
        finally
        {
            try { tmp.Delete(recursive: true); } catch { /* ignore */ }
            IdeSettingsStore.Unset(IdeFindChannel.LastKey);
        }
    }

    [Fact]
    public void Paths_multi_root_via_FindInFiles()
    {
        var a = Directory.CreateTempSubdirectory("cdp-find-a-");
        var b = Directory.CreateTempSubdirectory("cdp-find-b-");
        try
        {
            File.WriteAllText(Path.Combine(a.FullName, "a.txt"), "multi-root-needle-7\n");
            File.WriteAllText(Path.Combine(b.FullName, "b.txt"), "multi-root-needle-7\n");

            var store = new DocumentBufferStore();
            var session = new SessionContext();
            var json = FindInFiles.Dispatch(
                store,
                session,
                new Dictionary<string, JsonElement>
                {
                    ["scope"] = JsonSerializer.SerializeToElement("external"),
                    ["path"] = JsonSerializer.SerializeToElement(a.FullName),
                    ["paths"] = JsonSerializer.SerializeToElement(new[] { a.FullName, b.FullName }),
                    ["query"] = JsonSerializer.SerializeToElement("multi-root-needle-7"),
                    ["peek"] = JsonSerializer.SerializeToElement(false),
                    ["max"] = JsonSerializer.SerializeToElement(10)
                },
                all: true);

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean(), json);
            Assert.True(doc.RootElement.GetProperty("count").GetInt32() >= 2, json);
        }
        finally
        {
            try { a.Delete(recursive: true); } catch { /* ignore */ }
            try { b.Delete(recursive: true); } catch { /* ignore */ }
        }
    }
}
