#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenBrowserHostTests
{
    [Fact]
    public void Route_browser_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("browser");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Browser, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("browser", r.Go);
    }

    [Fact]
    public void Route_browser_open_requires_url()
    {
        var missing = CitizenIntentRouter.RouteOne("browser open");
        Assert.False(missing.Ok);
        Assert.Equal("browser_url_required", missing.Reason);
    }

    [Fact]
    public void Route_browser_search_requires_query()
    {
        var missing = CitizenIntentRouter.RouteOne("browser search");
        Assert.False(missing.Ok);
        Assert.Equal("browser_query_required", missing.Reason);
    }

    [Fact]
    public void Route_browser_open_url_ok()
    {
        var r = CitizenIntentRouter.RouteOne("browser open url=\"https://example.com\"");
        Assert.True(r.Ok);
        Assert.Equal("open", r.Op);
    }

    [Fact]
    public void Route_internet_browser_search_q_ok()
    {
        var r = CitizenIntentRouter.RouteOne("internet_browser search q=\"cdp lynx\"");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Browser, r.Verb);
        Assert.Equal("search", r.Op);
    }

    [Fact]
    public void Execute_browser_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.BrowserDispatchOverride = _ =>
            """{"schema":"internet_browser_scene/v1","ok":true,"op":"scene","active_tab":"main","tab_count":1,"pulse":"browser · idle"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("browser scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("browser", applied[0].Action);
            Assert.Contains("browser", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.BrowserDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_browser_search_passes_q()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.BrowserDispatchOverride = args =>
        {
            seen = args;
            return """{"schema":"internet_browser_scene/v1","ok":true,"op":"search","query":"peer net","url":"https://html.duckduckgo.com/html/?q=peer+net"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("browser search q=\"peer net\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("search", seen!["op"].GetString());
            Assert.Equal("peer net", seen["q"].GetString());
        }
        finally
        {
            CitizenRouteHost.BrowserDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
