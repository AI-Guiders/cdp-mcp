#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenContextHostTests
{
    [Fact]
    public void Route_context_alone()
    {
        var r = CitizenIntentRouter.RouteOne("context");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Context, r.Verb);
        Assert.Equal("context", r.Go);
        Assert.Null(r.Scene);
        Assert.Null(r.Organ);
    }

    [Fact]
    public void Route_desk_cdp_and_args()
    {
        var desk = CitizenIntentRouter.RouteOne("context_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Context, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_context");
        Assert.True(cdp.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Context, cdp.Verb);

        var set = CitizenIntentRouter.RouteOne("context phase=act object=code intent=change language=csharp");
        Assert.True(set.Ok);
        Assert.Equal("act", set.Scene);
        Assert.Equal("code", set.Organ);
        Assert.Equal("change", set.Detail);
        Assert.Equal("csharp", set.Tool);

        var get = CitizenIntentRouter.RouteOne("cdp_context get=true");
        Assert.True(get.Ok);
        Assert.Equal("get", get.Op);

        var hold = CitizenIntentRouter.RouteOne("context phase=plan layout_hold=true");
        Assert.True(hold.Ok);
        Assert.Equal("plan", hold.Scene);
        Assert.Equal("layout_hold", hold.Cmd);
    }

    [Fact]
    public void Route_does_not_steal_go_context()
    {
        var go = CitizenIntentRouter.RouteOne("go=context");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("context", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Context, go.Verb);
    }

    [Fact]
    public void Execute_context_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.ContextDispatchOverride = _ =>
                """{"ok":true,"pulse":"context · explore/code","phase":"explore","object":"code"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("context")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("context", applied[0].Action);
            Assert.Equal("context", applied[0].Go);
            Assert.Contains("context", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ContextDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_context_passes_phase_object_get()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.ContextDispatchOverride = args =>
            {
                seen = args;
                return """{"ok":true,"pulse":"context · act/code","phase":"act","object":"code"}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_context phase=act object=code get=true")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("act", seen!["phase"].GetString());
            Assert.Equal("code", seen["object"].GetString());
            Assert.True(seen["get"].GetBoolean());
        }
        finally
        {
            CitizenRouteHost.ContextDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_context_fallback_pulse_from_fields()
    {
        try
        {
            CitizenRouteHost.ContextDispatchOverride = _ =>
                """{"ok":true,"phase":"verify","object":"repo"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("context")]);
            Assert.True(applied[0].Ok);
            Assert.Contains("verify", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("repo", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ContextDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_context_tolerates_meta_list_changed_tail()
    {
        try
        {
            CitizenRouteHost.ContextDispatchOverride = _ =>
                "{\"phase\":\"explore\",\"object\":\"code\"}\n# list_changed: shortlist refreshed for new context\n# desk_layout: held";

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_context phase=explore object=code layout_hold=true")]);
            Assert.True(applied[0].Ok);
            Assert.Contains("explore", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("held", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.ContextDispatchOverride = null;
        }
    }
}
