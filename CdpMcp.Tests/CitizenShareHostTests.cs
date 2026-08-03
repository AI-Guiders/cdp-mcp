#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenShareHostTests
{
    [Fact]
    public void Route_share_bare()
    {
        var r = CitizenIntentRouter.RouteOne("share");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Share, r.Verb);
        Assert.Equal("share", r.Op);
    }

    [Fact]
    public void Route_share_with_and_path()
    {
        var r = CitizenIntentRouter.RouteOne("share with=operator path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("operator", r.Detail);
    }

    [Fact]
    public void Route_share_from_self()
    {
        var r = CitizenIntentRouter.RouteOne("share from=self");
        Assert.True(r.Ok);
        Assert.Equal("self", r.Detail);
    }

    [Fact]
    public void Execute_share_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ShareCallOverride = args =>
        {
            seen = args;
            return """{"schema":"share/v1","ok":true,"op":"share","with":"operator","status":"shared","chars":42,"path":"D:\\tmp\\share.md"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("share with=operator path=a.cs")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("share", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("share", seen!["op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Equal("operator", seen["with"].GetString());
            Assert.Contains("operator", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("shared", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("chars=42", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ShareCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_share_surfaces_error()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ShareCallOverride = _ =>
            """{"schema":"share/v1","ok":false,"op":"share","error":"unsupported_with","with":"peer"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("share with=peer")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("unsupported_with", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ShareCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
