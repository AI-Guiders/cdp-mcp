#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenFindBufHostTests
{
    [Fact]
    public void Route_find_all_keys()
    {
        var r = CitizenIntentRouter.RouteOne("find_all path=a.cs query=foo");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FindBuf, r.Verb);
        Assert.Equal("find_all", r.Op);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("foo", r.OldString);
        Assert.Equal("buffer", r.Detail);
    }

    [Fact]
    public void Route_buf_find_keys()
    {
        var r = CitizenIntentRouter.RouteOne("buf_find path=a.cs query=Needle");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FindBuf, r.Verb);
        Assert.Equal("find", r.Op);
        Assert.Equal("Needle", r.OldString);
    }

    [Fact]
    public void Route_find_scope_buffer_not_ide_find()
    {
        var r = CitizenIntentRouter.RouteOne("find query=x scope=buffer path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FindBuf, r.Verb);
    }

    [Fact]
    public void Route_bare_find_stays_ide_find()
    {
        var r = CitizenIntentRouter.RouteOne("find query=IdeFindChannel where=project");
        Assert.Equal(CitizenIntentRouter.Verb.Find, r.Verb);
    }

    [Fact]
    public void Route_find_all_requires_query()
    {
        var r = CitizenIntentRouter.RouteOne("find_all path=a.cs");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.FindBuf, r.Verb);
        Assert.Contains("query", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_find_all_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.FindBufCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"find_all","count":2,"scope":"buffer","meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("find_all path=a.cs query=foo")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("find_all", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("find_all", seen!["op"].GetString());
            Assert.Equal("foo", seen["query"].GetString());
            Assert.Equal("buffer", seen["scope"].GetString());
            Assert.Contains("n=2", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.FindBufCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_buf_find_surfaces_query_empty()
    {
        CitizenRouteHost.UnbindLifecycle();
        try
        {
            var route = CitizenIntentRouter.RouteOne("buf_find path=a.cs");
            Assert.False(route.Ok);
            var applied = CitizenRouteHost.Execute([route]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("query", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_find_buf_override_failure()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.FindBufCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"find","error":"query_required"}""";
        try
        {
            var route = CitizenIntentRouter.RouteOne("buf_find path=a.cs query=x");
            Assert.True(route.Ok);
            var applied = CitizenRouteHost.Execute([route]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("query_required", applied[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.FindBufCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
