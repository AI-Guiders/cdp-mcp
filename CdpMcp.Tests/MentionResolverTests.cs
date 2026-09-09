using Xunit;

namespace CdpMcp.Tests;

/// <summary>Owning layer of mention semantics (Света 2026-09-08, DDD) — все поверхности
/// берут решение отсюда: parse → roster → self-skip → excerpt вокруг своего упоминания.</summary>
[Collection(nameof(IntercomLatchSerial))]
public sealed class MentionResolverTests : IDisposable
{
    readonly string _root;

    public MentionResolverTests()
    {
        // Ростер — witdb-файл у StateRoot; тест герметично строит свой корень + садит ники.
        _root = Path.Combine(Path.GetTempPath(), "cdp-icm-mention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideIntercomVoiceLatch.RootOverrideForTests = _root;
        foreach (var nick in new[] { "Тень", "Ток" })
            Assert.NotNull(CideIntercomAgents.Claim(nick, kind: "agent", lineId: null,
                harness: "pytest", session: null));
    }

    public void Dispose()
    {
        CideIntercomVoiceLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Author_Never_Wakes_Himself()
    {
        var mentions = MentionResolver.Resolve("@Тихон привет себе.", "Тихон");
        Assert.Empty(mentions);
    }

    [Fact]
    public void Author_With_AtPrefix_Normailzed_Self_Skip()
    {
        // Тень-кейс: nick="@Тень" + упоминание "Тень" — self-skip обязан сработать.
        var mentions = MentionResolver.Resolve("@Тень привет себе.", "@Тень");
        Assert.Empty(mentions);
    }

    [Fact]
    public void Backtick_Span_Is_Not_An_Address()
    {
        var mentions = MentionResolver.Resolve("Урок: `cdp_intercom op=sub nick=@Тень` — команда.", "Тихон");
        Assert.Empty(mentions);
    }

    [Fact]
    public void Registered_Nick_Resolves_With_Own_Excerpt()
    {
        var mentions = MentionResolver.Resolve("@Тень — письмо по теме.", "Тихон");
        var m = Assert.Single(mentions);
        Assert.Equal("Тень", m.Nick);
        Assert.Contains("письмо по теме", m.Excerpt);
    }

    [Fact]
    public void Multi_Mention_Each_Letter_Carries_Own_Context()
    {
        var body = "@Ток — начало про Тока. " + new string('x', 200) +
                   " @Тень — а это абзац про Тень глубоко в посте.";

        var forTen = MentionResolver.Resolve(body, "Тихон").Single(m => m.Nick == "Тень").Excerpt;
        var forTok = MentionResolver.Resolve(body, "Тихон").Single(m => m.Nick == "Ток").Excerpt;

        Assert.Contains("про Тень", forTen);
        Assert.DoesNotContain("про Тока", forTen);
        Assert.Contains("про Тока", forTok);
        Assert.DoesNotContain("про Тень", forTok);
    }

    [Fact]
    public void Unknown_Nick_Drops_Silently()
    {
        var mentions = MentionResolver.Resolve("@Незнакомец привет.", "Тихон");
        Assert.Empty(mentions);
    }
}
