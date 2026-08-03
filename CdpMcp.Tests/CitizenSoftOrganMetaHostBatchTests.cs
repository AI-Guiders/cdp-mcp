#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSoftOrganMetaHostBatchTests
{
    [Theory]
    [InlineData("md_author", "scene", "md_author")]
    [InlineData("cdp_md_author check path=docs/a.md", "check", "md_author")]
    [InlineData("project_switch", "scene", "project_switch")]
    [InlineData("ps set primary=foo scope=bar", "set", "project_switch")]
    [InlineData("glass", "scene", "surface_desk")]
    [InlineData("cdp_glass layout", "layout", "surface_desk")]
    [InlineData("fdr tail", "tail", "fdr")]
    [InlineData("teeth explain", "explain", "teeth")]
    [InlineData("postmortem template", "template", "postmortem")]
    [InlineData("plugins search q=roslyn", "search", "plugins")]
    [InlineData("problems", "list", "problems")]
    [InlineData("errlist", "list", "problems")]
    public void Route_aliases_and_ops(string raw, string expectedOp, string expectedGo)
    {
        var r = CitizenIntentRouter.RouteOne(raw);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(expectedGo, r.Go);
        Assert.Equal(expectedOp, r.Op);
    }

    [Fact]
    public void Route_no_steal_bare_verbs()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.MdAuthor, CitizenIntentRouter.RouteOne("check").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.MdAuthor, CitizenIntentRouter.RouteOne("expand").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.MdAuthor, CitizenIntentRouter.RouteOne("export").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.MdAuthor, CitizenIntentRouter.RouteOne("scene").Verb);

        Assert.Equal(CitizenIntentRouter.Verb.Sniper, CitizenIntentRouter.RouteOne("scope").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Scope, CitizenIntentRouter.RouteOne("scope").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Scope, CitizenIntentRouter.RouteOne("set primary=foo").Verb);

        Assert.NotEqual(CitizenIntentRouter.Verb.Glass, CitizenIntentRouter.RouteOne("layout").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Glass, CitizenIntentRouter.RouteOne("focus").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Glass, CitizenIntentRouter.RouteOne("run").Verb);

        Assert.NotEqual(CitizenIntentRouter.Verb.Plugins, CitizenIntentRouter.RouteOne("list").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Plugins, CitizenIntentRouter.RouteOne("search").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Plugins, CitizenIntentRouter.RouteOne("enable").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Plugins, CitizenIntentRouter.RouteOne("install").Verb);

        Assert.NotEqual(CitizenIntentRouter.Verb.Problems, CitizenIntentRouter.RouteOne("row").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Problems, CitizenIntentRouter.RouteOne("aim").Verb);
    }

    [Fact]
    public void Route_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("fdr boom");
        Assert.False(r.Ok);
        Assert.Equal("fdr_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_md_author_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.MdAuthorHandleOverride = (_, _) =>
            new { schema = "md_author/v0", ok = true, op = "scene", pulse = "md_author · idle" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("md_author")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("md_author", applied[0].Action);
        }
        finally
        {
            CitizenRouteHost.MdAuthorHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_scope_set_passes_primary()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ScopeHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "scope_channel/v0", ok = true, op = "set", pulse = "ps · set" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("ps set primary=foo scope=bar")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("set", seen!["op"].GetString());
            Assert.Equal("foo", seen["primary"].GetString());
            Assert.Equal("bar", seen["scope"].GetString());
        }
        finally
        {
            CitizenRouteHost.ScopeHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_glass_layout_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.GlassHandleOverride = (_, _) =>
            new { schema = "agent_surface/v0", ok = true, op = "layout", pulse = "surface · layout" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("glass layout")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("surface_desk", applied[0].Go);
        }
        finally
        {
            CitizenRouteHost.GlassHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_plugins_list_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.PluginsHandleOverride = (_, _, _) =>
            new { schema = "plugins_channel/v1", ok = true, pulse = "plugins · empty" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("plugins")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("plugins", applied[0].Action);
        }
        finally
        {
            CitizenRouteHost.PluginsHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_problems_row_passes_args()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ProblemsHandleOverride = (_, _, args) =>
        {
            seen = args;
            return new { schema = "problems_channel/v1", ok = true, pulse = "problems · 0E", rows = Array.Empty<object>() };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("problems row=p1")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("p1", seen!["row"].GetString());
        }
        finally
        {
            CitizenRouteHost.ProblemsHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
