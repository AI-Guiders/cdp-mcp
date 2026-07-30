using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgnitePagePickTests
{
    [Theory]
    [InlineData("awareness-as-countermeasure-v1.md - casa-dev (Workspace) - Cursor", true)]
    [InlineData("Program.cs - cdp-mcp - Cursor", true)]
    [InlineData("note.toml - workspace (Workspace) - Cursor", true)]
    [InlineData("Cursor Agents", false)]
    [InlineData("", false)]
    public void LooksLikeEditorTab_samples(string title, bool expected)
        => Assert.Equal(expected, IdeIgniteChannel.LooksLikeEditorTab(title));

    [Fact]
    public void RankPageTargets_prefers_Cursor_Agents_over_md_editor()
    {
        var list = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "page",
                title = "awareness-as-countermeasure-v1.md - casa-dev (Workspace) - Cursor",
                url = "vscode-file://vscode-app/c:/Program Files/cursor/resources/app/out/vs/code/electron-sandbox/workbench/workbench.html",
                webSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/MD",
            },
            new
            {
                type = "page",
                title = "Cursor Agents",
                url = "vscode-file://vscode-app/c:/Program Files/cursor/resources/app/out/vs/code/electron-sandbox/workbench/workbench.html",
                webSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/AGENTS",
            },
            new { type = "worker", title = "", webSocketDebuggerUrl = "ws://127.0.0.1:9222/devtools/page/W" },
        });

        var ranked = IdeIgniteChannel.RankPageTargets(list);

        Assert.NotEmpty(ranked);
        Assert.Equal("Cursor Agents", ranked[0].Title);
        Assert.DoesNotContain(ranked, p => p.Title.Contains(".md", StringComparison.OrdinalIgnoreCase));
        Assert.True(ranked[0].Score > 0);
    }

    [Fact]
    public void ScoreIgnitePage_agents_beats_generic()
    {
        Assert.True(
            IdeIgniteChannel.ScoreIgnitePage("Cursor Agents")
            > IdeIgniteChannel.ScoreIgnitePage(""));
        Assert.True(IdeIgniteChannel.ScoreIgnitePage("foo.md - x - Cursor") < 0);
    }
}
