#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class BufferLanguageRulesTests
{
    [Theory]
    [InlineData("Module.fs", "fsharp")]
    [InlineData("Deck.catalog.gdl", "gdl")]
    [InlineData("App.cs", "csharp")]
    [InlineData("index.ts", "typescript")]
    [InlineData("script.ps1", "powershell")]
    public void GuessLanguage_uses_federation_path_rules(string fileName, string expected)
    {
        Assert.Equal(expected, BufferLanguageRules.GuessLanguage(fileName));
    }

    [Fact]
    public void Resolve_prefers_path_over_stale_text_buffer_language()
    {
        var resolved = BufferLanguageRules.Resolve(@"D:\repo\Kernel.fs", "text");
        Assert.Equal("fsharp", resolved);
    }

    [Theory]
    [InlineData("fsharp", true)]
    [InlineData("gdl", true)]
    [InlineData("csharp", false)]
    public void IsLrcLanguage_matches_federation_backends(string language, bool expected)
    {
        Assert.Equal(expected, BufferLanguageRules.IsLrcLanguage(language));
    }

    [Theory]
    [InlineData("fsharp", true)]
    [InlineData("gdl", true)]
    [InlineData("csharp", true)]
    [InlineData("text", false)]
    public void SupportsOnlineBufferDiagnostics_includes_lrc(string language, bool expected)
    {
        Assert.Equal(expected, BufferLanguageRules.SupportsOnlineBufferDiagnostics(language));
    }
}
