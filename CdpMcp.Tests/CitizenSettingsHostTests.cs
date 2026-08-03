#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSettingsHostTests
{
    [Fact]
    public void Route_settings_alone_is_options()
    {
        var r = CitizenIntentRouter.RouteOne("settings");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Settings, r.Verb);
        Assert.Equal("options", r.Op);
        Assert.Equal("settings", r.Go);
    }

    [Fact]
    public void Route_options_and_languages_page()
    {
        var options = CitizenIntentRouter.RouteOne("options");
        Assert.True(options.Ok);
        Assert.Equal("options", options.Op);

        var page = CitizenIntentRouter.RouteOne("settings page page=desk");
        Assert.True(page.Ok);
        Assert.Equal("page", page.Op);
        Assert.Equal("desk", page.Path);

        var languages = CitizenIntentRouter.RouteOne("languages");
        Assert.True(languages.Ok);
        Assert.Equal("page", languages.Op);
        Assert.Equal("languages", languages.Path);
    }

    [Fact]
    public void Route_get_set_require_fields()
    {
        var get = CitizenIntentRouter.RouteOne("settings get key=browser.search_engine");
        Assert.True(get.Ok);
        Assert.Equal("get", get.Op);
        Assert.Equal("browser.search_engine", get.Path);

        var miss = CitizenIntentRouter.RouteOne("settings set key=browser.search_engine");
        Assert.False(miss.Ok);
        Assert.Equal("settings_key_value_required", miss.Reason);

        var set = CitizenIntentRouter.RouteOne("settings set key=browser.search_engine value=ddg");
        Assert.True(set.Ok);
        Assert.Equal("set", set.Op);
        Assert.Equal("ddg", set.Tool);
    }

    [Fact]
    public void Route_unknown_and_compounds()
    {
        var bad = CitizenIntentRouter.RouteOne("settings boom");
        Assert.False(bad.Ok);
        Assert.Equal("settings_op_unknown", bad.Reason);

        var compound = CitizenIntentRouter.RouteOne("settings_get key=desk.default_layout");
        Assert.True(compound.Ok);
        Assert.Equal("get", compound.Op);
        Assert.Equal("desk.default_layout", compound.Path);

        var lsp = CitizenIntentRouter.RouteOne("lsp_probe id=python");
        Assert.True(lsp.Ok);
        Assert.Equal("lsp_probe", lsp.Op);
        Assert.Equal("python", lsp.Tool);
    }

    [Fact]
    public void Execute_settings_options_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.SettingsDispatchOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"options","title":"Tools → Options","pulse":"settings options ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("settings")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("settings", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("options", seen!["op"].GetString());
            Assert.Contains("settings", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.SettingsDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_settings_page_passes_page()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.SettingsDispatchOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"page","page":"desk"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("settings page page=desk")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("page", seen!["op"].GetString());
            Assert.Equal("desk", seen["page"].GetString());
        }
        finally
        {
            CitizenRouteHost.SettingsDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
