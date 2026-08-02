#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenIdeHostTests
{
    [Fact]
    public void Route_goto_requires_path()
    {
        var r = CitizenIntentRouter.RouteOne("goto");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ide, r.Verb);
        Assert.Equal("ide_path_required", r.Reason);
    }

    [Fact]
    public void Route_goto_requires_line()
    {
        var r = CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs");
        Assert.False(r.Ok);
        Assert.Equal("ide_line_required", r.Reason);
    }

    [Fact]
    public void Route_goto_ok()
    {
        var r = CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs line=10 column=5");
        Assert.True(r.Ok);
        Assert.Equal("go_to_definition", r.Op);
        Assert.Equal("CitizenRouteHost.cs", r.Path);
    }

    [Fact]
    public void Route_ide_usages_ok()
    {
        var r = CitizenIntentRouter.RouteOne("ide usages path=X.cs line=2");
        Assert.True(r.Ok);
        Assert.Equal("find_usages", r.Op);
    }

    [Fact]
    public void Route_diagnostics_ok_without_line()
    {
        var r = CitizenIntentRouter.RouteOne("diagnostics path=X.cs");
        Assert.True(r.Ok);
        Assert.Equal("get_diagnostics", r.Op);
    }

    [Fact]
    public void Execute_ide_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IdeCallOverride = (op, args) =>
        {
            Assert.Equal("go_to_definition", op);
            Assert.Equal("CitizenRouteHost.cs", args["file_path"].GetString());
            Assert.Equal(10, args["line"].GetInt32());
            return Task.FromResult("""{"locations":[{"path":"A.cs","line":1}]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("goto path=CitizenRouteHost.cs line=10")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("ide", applied[0].Action);
            Assert.Contains("loc", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
