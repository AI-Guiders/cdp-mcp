#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenHciHostTests
{
    [Fact]
    public void Route_hci_alone_is_status()
    {
        var r = CitizenIntentRouter.RouteOne("hci");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Hci, r.Verb);
        Assert.Equal("status", r.Op);
        Assert.Equal("hci", r.Go);
    }

    [Fact]
    public void Route_hci_search_parses_query()
    {
        var r = CitizenIntentRouter.RouteOne("hci search query=CitizenRouteHost");
        Assert.True(r.Ok);
        Assert.Equal("search", r.Op);
        Assert.Equal("CitizenRouteHost", r.NewString);
    }

    [Fact]
    public void Route_hci_search_requires_query()
    {
        var r = CitizenIntentRouter.RouteOne("hci search");
        Assert.False(r.Ok);
        Assert.Equal("hci_query_required", r.Reason);
    }

    [Fact]
    public void Route_codebase_index_alias()
    {
        var r = CitizenIntentRouter.RouteOne("codebase_index status");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Hci, r.Verb);
        Assert.Equal("status", r.Op);
    }

    [Fact]
    public void Route_explain_requires_hit_id()
    {
        var r = CitizenIntentRouter.RouteOne("hci explain");
        Assert.False(r.Ok);
        Assert.Equal("hci_hit_id_required", r.Reason);
    }

    [Fact]
    public void Execute_hci_without_backend_fails_disabled()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("hci")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("hci", applied[0].Action);
        Assert.Equal("hci_backend_disabled", applied[0].Reason);
    }

    [Fact]
    public void Execute_hci_search_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.HciCallOverride = (tool, args) =>
        {
            Assert.Equal("codebase_index_search", tool);
            Assert.True(args.ContainsKey("query"));
            return Task.FromResult("""{"ok":true,"count":2,"hits":[]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne("hci search query=CitizenRouteHost")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("hci", applied[0].Action);
            Assert.Contains("2 hit", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
