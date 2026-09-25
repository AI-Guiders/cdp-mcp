using Cdp.Lsp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class LspRecipeCatalogTests
{
    [Fact]
    public void MergeStartupPresets_includes_latex_vertical_recipe()
    {
        var merged = LspOptionsToolkit.MergeStartupPresets(LspLaunchPreset.BuiltInDefaults);
        var latex = merged.FirstOrDefault(p => p.Id.Equals("latex", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(latex);
        Assert.Equal("texlab", latex.Command);
        Assert.Contains("latex", latex.LanguageIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("bibtex", latex.LanguageIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MergeStartupPresets_includes_markdown_vertical_recipe()
    {
        var merged = LspOptionsToolkit.MergeStartupPresets(LspLaunchPreset.BuiltInDefaults);
        Assert.Contains(merged, p => p.Id.Equals("markdown", StringComparison.OrdinalIgnoreCase));
    }
}
