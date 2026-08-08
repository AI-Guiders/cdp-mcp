#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenLeafPathRewriteTests
{
    [Theory]
    [InlineData("GlassIntercom.cs")]
    [InlineData("GlassIntercomHost.cs")]
    [InlineData("CascadeIDE.cs")]
    [InlineData("CitizenRouteHost.cs")]
    [InlineData("CitizenRouteHost.Intercom.cs")]
    [InlineData("GlassIntercomMention.cs")]
    [InlineData("path=GlassIntercom.cs")]
    public void RewriteInventedTakePath_maps_siblings_to_leaf(string poison)
    {
        var path = poison.StartsWith("path=", StringComparison.Ordinal)
            ? poison["path=".Length..]
            : poison;
        Assert.Equal(
            CitizenResultWake.LeafTakePath,
            CitizenResultWake.RewriteInventedTakePath(path));
    }

    [Fact]
    public void RouteTake_rewrites_invented_basename()
    {
        // RouteOne gets IntentText (wire already stripped @intent).
        var route = CitizenIntentRouter.RouteOne("take path=GlassIntercom.cs start_line=60 end_line=120");
        Assert.True(route.Ok);
        Assert.Equal(CitizenResultWake.LeafTakePath, route.Path);
    }

    [Fact]
    public void RewriteInventedTakePath_keeps_real_leaf()
    {
        Assert.Equal(
            CitizenResultWake.LeafTakePath,
            CitizenResultWake.RewriteInventedTakePath(CitizenResultWake.LeafTakePath));
    }
}
