using Xunit;
namespace CdpMcp.Tests;

public sealed class IdeWorldChannelTests
{
    [Theory]
    [InlineData("git", true)]
    [InlineData("git_scene", true)]
    [InlineData("shell", true)]
    [InlineData("shell_scene", true)]
    [InlineData("browser", true)]
    [InlineData("net", true)]
    [InlineData("mcp", true)]
    [InlineData("mcp_scene", true)]
    [InlineData("editor", false)]
    [InlineData("editor_scene", false)]
    [InlineData("test", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsWorldOrgan_classifies_seat_organs(string? organ, bool expected)
        => Assert.Equal(expected, IdeWorldChannel.IsWorldOrgan(organ));

    [Theory]
    [InlineData("git", true)]
    [InlineData("git_scene", true)]
    [InlineData("shell", true)]
    [InlineData("shell_scene", true)]
    [InlineData("browser", true)]
    [InlineData("net", true)]
    [InlineData("internet", true)]
    [InlineData("mcp", true)]
    [InlineData("mcp_scene", true)]
    [InlineData("git_status", false)]
    [InlineData("shell_run", false)]
    [InlineData("editor", false)]
    [InlineData(null, false)]
    public void IsWorldSceneGo_only_scene_verbs(string? verb, bool expected)
        => Assert.Equal(expected, IdeWorldChannel.IsWorldSceneGo(verb));

    [Fact]
    public void Pane_marks_world_pulse_contract()
    {
        var pane = IdeWorldChannel.Pane("git", ok: true, pulse: "clean");
        var json = System.Text.Json.JsonSerializer.Serialize(pane);
        Assert.Contains("\"world\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"detail\":\"pulse\"", json, StringComparison.Ordinal);
        Assert.Contains("\"go\":\"git\"", json, StringComparison.Ordinal);
    }
}
