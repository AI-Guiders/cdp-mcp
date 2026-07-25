using Xunit;
namespace CdpMcp.Tests;

public sealed class IdeDeskViewOrganNeedsProjectTests
{
    [Theory]
    [InlineData("shell", true)]
    [InlineData("shell_scene", true)]
    [InlineData("mcp", true)]
    [InlineData("mcp_scene", true)]
    [InlineData("git", true)]
    [InlineData("git_scene", true)]
    [InlineData("browser", true)]
    [InlineData("editor", true)]
    [InlineData("editor_scene", true)]
    [InlineData("plan", false)]
    [InlineData("report", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void OrganNeedsProject_includes_world_seats(string? organ, bool expected)
        => Assert.Equal(expected, IdeDeskView.OrganNeedsProject(organ));
}
