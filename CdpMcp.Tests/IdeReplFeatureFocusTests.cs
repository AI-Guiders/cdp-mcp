using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeReplFeatureFocusTests
{
    static readonly Dictionary<string, JsonElement> Empty = new(StringComparer.Ordinal);

    [Fact]
    public void Feature_at_focus_strips_directive_from_title()
    {
        var applied = IdeRepl.Apply("feature night-refactor @focus", Empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);

        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        using var doc = JsonDocument.Parse(ga.GetRawText());
        Assert.Equal("night-refactor", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("feature", doc.RootElement.GetProperty("op").GetString());
    }

    [Theory]
    [InlineData("focus", "Y")]
    [InlineData("done", "ship")]
    [InlineData("park", "later")]
    public void SplitTitlePhase_strips_tm_directives(string directive, string name)
    {
        var (title, phase) = IdeRepl.SplitTitlePhase([name, "@" + directive]);
        Assert.Equal(name, title);
        Assert.Null(phase);
    }

    [Fact]
    public void SplitTitlePhase_keeps_phase_affinity()
    {
        var (title, phase) = IdeRepl.SplitTitlePhase(["omit-tiles", "@act"]);
        Assert.Equal("omit-tiles", title);
        Assert.Equal("act", phase);
    }
}
