#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenReplaceAllHostTests
{
    [Fact]
    public void Route_replace_all_keys()
    {
        var r = CitizenIntentRouter.RouteOne("replace_all path=a.cs query=foo text=bar");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.ReplaceAll, r.Verb);
        Assert.Equal("replace_all", r.Op);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("foo", r.OldString);
        Assert.Equal("bar", r.NewString);
    }

    [Fact]
    public void Route_replace_all_requires_query()
    {
        var r = CitizenIntentRouter.RouteOne("replace_all path=a.cs text=bar");
        Assert.False(r.Ok);
        Assert.Contains("query", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Route_replace_all_before_pathmutate_replace()
    {
        var r = CitizenIntentRouter.RouteOne("replace_all path=a.cs query=x text=y");
        Assert.Equal(CitizenIntentRouter.Verb.ReplaceAll, r.Verb);
    }

    [Fact]
    public void Execute_replace_all_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ReplaceAllCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"replace_all","replaced":3,"meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("replace_all path=a.cs query=foo text=bar")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("replace_all", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("replace_all", seen!["op"].GetString());
            Assert.Equal("foo", seen["query"].GetString());
            Assert.Equal("bar", seen["text"].GetString());
            Assert.Contains("n=3", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ReplaceAllCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_replace_all_surfaces_query_required()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ReplaceAllCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"replace_all","error":"query_required"}""";
        try
        {
            // Force host path with a routed-ok intent but override fails
            var route = CitizenIntentRouter.RouteOne("replace_all path=a.cs query=x text=y");
            Assert.True(route.Ok);
            var applied = CitizenRouteHost.Execute([route]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("query_required", applied[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ReplaceAllCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
