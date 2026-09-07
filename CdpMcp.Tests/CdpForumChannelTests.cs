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
        // Кейс Тени (Света 2026-09-08): mention-wake в тестах не должен класть
        // реальные письма в прод-очередь — иначе Тень получает «echo» на каждый
        // прогон тестов (класс empty user messages).
        CideWakeDispatch.StorePathOverrideForTests = () => Path.Combine(_root, "wake-dispatch.json");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CDP_FORUM_ROOT", null);
        CideWakeDispatch.StorePathOverrideForTests = null;
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
    public void Post_With_Mention_Produces_Wake_Letter()
    {
        // Механика: пост с @зарегистрированным-ником кладёт wake-конверт (канон
        // CideIntercomAgents.MentionsOf — общий для intercom/форума/всех поверхностей).
        // Здесь — через живой реестр этой машины: @Тень зарегистрирован (ADR-0212).
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"), "# Тема 001: Test\nСтатус: open\n");

        var post = JsonDocument.Parse(Handle("""{ "op": "post", "thread": "001", "body": "@Тень — письмо по теме.", "nick": "Тихон" }""")).RootElement;
        Assert.True(post.GetProperty("ok").GetBoolean());
        Assert.True(post.GetProperty("wakes").GetInt32() >= 1, "пост с упоминанием должен будить");
    }
    [Fact]
    public void Post_Self_Mention_With_AtPrefixed_Nick_No_Self_Wake()
    {
        // Тень-кейс: nick="@Тень" (лишний @) + упоминание "Тень" — self-skip обязан сработать.
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"), "# Тема 001: Test\nСтатус: open\n");

        var post = JsonDocument.Parse(Handle("""{ "op": "post", "thread": "001", "body": "@Тень привет себе.", "nick": "@Тень" }""")).RootElement;
        Assert.True(post.GetProperty("ok").GetBoolean());
        Assert.Equal(0, post.GetProperty("wakes").GetInt32());
    }

    [Fact]
    public void Post_Quoted_Backtick_Mention_Does_Not_Wake()
    {
        // Цитата `@Ник` в бэктиках — не адресат (Тень писала ОБ упоминаниях — и будила).
        var dir = Path.Combine(_root, "topics", "001-test");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00-thread.md"), "# Тема 001: Test\nСтатус: open\n");

        var post = JsonDocument.Parse(Handle("""{ "op": "post", "thread": "001", "body": "Урок: `cdp_intercom op=sub nick=@Тень` — команда подписки.", "nick": "Тихон" }""")).RootElement;
        Assert.True(post.GetProperty("ok").GetBoolean());
        Assert.Equal(0, post.GetProperty("wakes").GetInt32());
    }
    [Fact]
    public void ExcerptAround_MultiMention_EachLetterCarriesOwnContext()
    {
        // Кейс Тени (Света 2026-09-08): пост упоминает нескольких линий, но общий
        // excerpt от начала показывал чужой абзац — письмо выглядело misdelivery.
        var body = "@Ток — начало про Тока. " + new string('x', 200) +
                   " @Тень — а это абзац про Тень глубоко в посте.";

        var forTen = CdpForumChannel.ExcerptAround(body, "Тень");
        var forTok = CdpForumChannel.ExcerptAround(body, "Ток");

        Assert.Contains("про Тень", forTen);
        Assert.DoesNotContain("про Тока", forTen);
        Assert.Contains("про Тока", forTok);
        Assert.DoesNotContain("про Тень", forTok);
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
