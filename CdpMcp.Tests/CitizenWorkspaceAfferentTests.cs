#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenWorkspaceAfferentTests
{
    [Fact]
    public void FormatSession_includes_leaf_lang_proj()
    {
        var line = CitizenWorkspaceAfferent.FormatSession(
            @"D:\Experiments\Personal Cursor Folder\Financial\software\open\cdp-mcp",
            "csharp",
            @"D:\Experiments\Personal Cursor Folder\Financial\software\open\cdp-mcp\CdpMcp.csproj");
        Assert.Contains("root=cdp-mcp", line, StringComparison.Ordinal);
        Assert.Contains("csharp", line, StringComparison.Ordinal);
        Assert.Contains("proj=CdpMcp.csproj", line, StringComparison.Ordinal);
        Assert.StartsWith("session |", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEditor_lists_focus_and_open()
    {
        var line = CitizenWorkspaceAfferent.FormatEditor(
            4,
            ["GlassIntercomMention.cs", "CitizenInventedHandsTests.cs", "CitizenGlassDialogBridgeTests.cs", "CitizenResultWakeTests.cs"],
            "GlassIntercomMention.cs");
        Assert.StartsWith("editor | 4 buf", line, StringComparison.Ordinal);
        Assert.Contains("focus=GlassIntercomMention.cs", line, StringComparison.Ordinal);
        Assert.Contains("open=GlassIntercomMention.cs, CitizenInventedHandsTests.cs, CitizenGlassDialogBridgeTests.cs", line, StringComparison.Ordinal);
        Assert.Contains("+1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEditor_empty_is_safe()
    {
        var line = CitizenWorkspaceAfferent.FormatEditor(0, [], null);
        Assert.Contains("0 buf", line, StringComparison.Ordinal);
        Assert.Contains("editor_scene", line, StringComparison.OrdinalIgnoreCase);
    }
}
