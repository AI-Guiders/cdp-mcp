#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenFindHostTests
{
    [Fact]
    public void Route_find_requires_query()
    {
        var r = CitizenIntentRouter.RouteOne("find");
        Assert.False(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Find, r.Verb);
        Assert.Equal("find_query_required", r.Reason);
    }

    [Fact]
    public void Route_find_query_keyed_ok()
    {
        var r = CitizenIntentRouter.RouteOne("find query=\"Verb.Find\" where=project shape=list");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Find, r.Verb);
        Assert.Equal("run", r.Op);
        Assert.Equal("Verb.Find", r.NewString);
        Assert.Equal("find_desk", r.Go);
    }

    [Fact]
    public void Route_find_positional_query_ok()
    {
        var r = CitizenIntentRouter.RouteOne("find IdeFindChannel where=project");
        Assert.True(r.Ok);
        Assert.Equal("IdeFindChannel", r.NewString);
        Assert.Equal("run", r.Op);
    }

    [Fact]
    public void Route_search_alias_ok()
    {
        var r = CitizenIntentRouter.RouteOne("search query=CitizenRouteHost");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Find, r.Verb);
        Assert.Equal("CitizenRouteHost", r.NewString);
    }

    [Fact]
    public void Route_find_last_ok()
    {
        var r = CitizenIntentRouter.RouteOne("find last");
        Assert.True(r.Ok);
        Assert.Equal("last", r.Op);
    }

    [Fact]
    public void Execute_find_without_store_fails()
    {
        CitizenRouteHost.UnbindLifecycle();
        IdeLanguageTools.BindDocumentStore(null);
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("find query=x")]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Equal("find", applied[0].Action);
            Assert.Equal("doc_store_unbound", applied[0].Reason);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
            IdeLanguageTools.BindDocumentStore(null);
        }
    }

    [Fact]
    public void Execute_find_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.FindCallOverride = args =>
        {
            Assert.Equal("run", args["op"].GetString());
            Assert.Equal("Verb.Find", args["query"].GetString());
            return new { ok = true, pulse = "find · project · 3 hit(s)", count = 3 };
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("find query=\"Verb.Find\" where=project")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("find", applied[0].Action);
            Assert.Contains("3 hit", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
