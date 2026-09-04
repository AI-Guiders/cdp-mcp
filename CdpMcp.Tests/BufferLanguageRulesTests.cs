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

    [Theory]
    [InlineData("AIGuiders.slnx", "xml")]
    [InlineData("CdpMcp.csproj", "xml")]
    [InlineData("Kernel.fsproj", "xml")]
    [InlineData("Directory.Build.props", "xml")]
    [InlineData("AIGuiders.sln", "text")]
    [InlineData("AIGuiders.slnf", "json")]
    public void GuessLanguage_never_feeds_solution_or_project_files_to_language_lsps(string fileName, string expected)
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
