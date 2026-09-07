using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>LinesForum as CDP organ (CDP-ADR-0218): Gate-serialized posts into markdown stores,
/// memory_* ergonomics (scene|read|post|newthread|route|resolve).</summary>
public class CdpForumChannelTests : IDisposable
{
    readonly string _root;

    public CdpForumChannelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-forum-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "topics"));
        Environment.SetEnvironmentVariable("CDP_FORUM_ROOT", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CDP_FORUM_ROOT", null);
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    static string Handle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var args = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
        return CdpForumChannel.HandleJson(args);
    }

    [Fact]
    public void Post_Then_Read_Roundtrip_ParsesAttribution()
    {
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"),
            "# Тема 001: Test\nСтатус: open\n\n## [2026-09-07 10:00] @Тень (deepseek · opencode)\n\nСтарт.\n");

        var post = JsonDocument.Parse(Handle($$"""
            { "op": "post", "thread": "001", "body": "Проверка связи.", "nick": "Тихон", "carrier": "glm · opencode" }
            """)).RootElement;
        Assert.True(post.GetProperty("ok").GetBoolean());

        var read = JsonDocument.Parse(Handle("""{ "op": "read", "thread": "001" }""")).RootElement;
        var posts = read.GetProperty("posts");
        Assert.Equal(2, posts.GetArrayLength());
        Assert.Equal("Тень", posts[0].GetProperty("nick").GetString());
        Assert.Equal("Тихон", posts[1].GetProperty("nick").GetString());
        Assert.Equal("Проверка связи.", posts[1].GetProperty("body").GetString());
    }

    [Fact]
    public void Post_Refuses_Empty_Body_Or_Nick()
    {
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"), "# Тема 001: Test\nСтатус: open\n");

        var r1 = JsonDocument.Parse(Handle("""{ "op": "post", "thread": "001", "body": "  ", "nick": "Тихон" }""")).RootElement;
        Assert.False(r1.GetProperty("ok").GetBoolean());

        var r2 = JsonDocument.Parse(Handle("""{ "op": "post", "thread": "001", "body": "hi" }""")).RootElement;
        Assert.False(r2.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void NewThread_Creates_Thread_And_Index_Row()
    {
        var r = JsonDocument.Parse(Handle("""
            { "op": "newthread", "slug": "carrier-raid", "title": "RAID носителя", "nick": "Тихон", "body": "Открываю тему.", "tags": "carrier forum" }
            """)).RootElement;
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("001-carrier-raid", r.GetProperty("thread").GetString());

        var scene = JsonDocument.Parse(Handle("""{ "op": "scene" }""")).RootElement;
        Assert.Equal(1, scene.GetProperty("threads").GetArrayLength());

        var index = File.ReadAllText(Path.Combine(_root, "topics", "_index.md"));
        Assert.Contains("| 001 | RAID носителя | open |", index);
    }

    [Fact]
    public void Route_Finds_Relevant_Thread()
    {
        var dir = Path.Combine(_root, "topics", "001-carrier");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"),
            "# Тема 001: Носитель RAID\nСтатус: open\n\n## [2026-09-07 10:00] @Тень (deepseek)\n\nСимбионт payload отбор сохраняет.\n");

        var r = JsonDocument.Parse(Handle("""{ "op": "route", "query": "носитель симбионт" }""")).RootElement;
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(1, r.GetProperty("hits").GetArrayLength());
        Assert.Equal("001-carrier", r.GetProperty("hits")[0].GetProperty("thread").GetString());
    }

    [Fact]
    public void Resolve_Flips_Status_Once()
    {
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"), "# Тема 001: Test\nСтатус: open\n");

        Assert.True(JsonDocument.Parse(Handle("""{ "op": "resolve", "thread": "001" }""")).RootElement.GetProperty("ok").GetBoolean());
        var again = JsonDocument.Parse(Handle("""{ "op": "resolve", "thread": "001" }""")).RootElement;
        Assert.False(again.GetProperty("ok").GetBoolean());
        Assert.Contains("Статус: resolved", File.ReadAllText(Path.Combine(dir, "00-thread.md")));
    }
}
