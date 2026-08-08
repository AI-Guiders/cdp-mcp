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
    public void PasteVerify_refuses_invented_siblings(string poison)
    {
        Assert.False(CitizenResultWake.TryPasteVerifyTakePath(poison, out _, out var refuse));
        Assert.Contains("paste_verify_leaf", refuse, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteTake_refuses_invented_basename()
    {
        var route = CitizenIntentRouter.RouteOne("take path=GlassIntercom.cs start_line=60 end_line=120");
        Assert.False(route.Ok);
        Assert.Contains("paste_verify_leaf", route.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PasteVerify_accepts_real_leaf_and_junction_normalize()
    {
        Assert.True(CitizenResultWake.TryPasteVerifyTakePath(
            CitizenResultWake.LeafTakePath, out var leaf, out var refuse));
        Assert.Null(refuse);
        Assert.Equal(CitizenResultWake.LeafTakePath, leaf);

        var mangled = CitizenResultWake.LeafTakePath.Replace(
            "Personal Cursor Folder", "Personal_Cursor_Folder", StringComparison.Ordinal);
        Assert.True(CitizenResultWake.TryPasteVerifyTakePath(mangled, out var fixedPath, out var refuse2));
        Assert.Null(refuse2);
        Assert.Equal(CitizenResultWake.LeafTakePath, fixedPath);
    }

    [Fact]
    public void RouteTake_accepts_quoted_leaf()
    {
        var route = CitizenIntentRouter.RouteOne(
            "take path=\"" + CitizenResultWake.LeafTakePath + "\" start_line=60 end_line=120");
        Assert.True(route.Ok);
        Assert.Equal(CitizenResultWake.LeafTakePath, route.Path);
    }

    [Fact]
    public void RewriteInventedTakePath_normalize_only_no_silent_map()
    {
        Assert.Equal(
            "GlassIntercom.cs",
            CitizenResultWake.RewriteInventedTakePath("GlassIntercom.cs"));
        Assert.Equal(
            CitizenResultWake.LeafTakePath,
            CitizenResultWake.RewriteInventedTakePath(CitizenResultWake.LeafTakePath));
    }
}
