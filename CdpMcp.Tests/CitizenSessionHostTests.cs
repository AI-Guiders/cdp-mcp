#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSessionHostTests
{
    [Fact]
    public void Route_session_alone()
    {
        var r = CitizenIntentRouter.RouteOne("session");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Session, r.Verb);
        Assert.Equal("session", r.Go);
        Assert.Null(r.Op);
    }

    [Fact]
    public void Route_aliases_and_include_pack()
    {
        var desk = CitizenIntentRouter.RouteOne("session_desk");
        Assert.Equal(CitizenIntentRouter.Verb.Session, desk.Verb);

        var cdp = CitizenIntentRouter.RouteOne("cdp_session");
        Assert.Equal(CitizenIntentRouter.Verb.Session, cdp.Verb);

        var pack = CitizenIntentRouter.RouteOne("session include_pack=true");
        Assert.True(pack.Ok);
        Assert.Equal("include_pack", pack.Op);
    }

    [Fact]
    public void Route_does_not_steal_session_context_or_go_session()
    {
        var ctx = CitizenIntentRouter.RouteOne("session_context");
        Assert.Equal(CitizenIntentRouter.Verb.Context, ctx.Verb);

        var go = CitizenIntentRouter.RouteOne("go=session");
        Assert.Equal(CitizenIntentRouter.Verb.Go, go.Verb);
        Assert.Equal("session", go.Go);
        Assert.NotEqual(CitizenIntentRouter.Verb.Session, go.Verb);
    }

    [Fact]
    public void Execute_session_places_and_pulses()
    {
        try
        {
            CitizenRouteHost.SessionDispatchOverride = _ =>
                """{"plane":"cdp_session","context":{"phase":"explore","object":"code"},"pack":{"available":true,"reason":"omitted_A"}}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("session")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("session", applied[0].Action);
            Assert.Equal("session", applied[0].Go);
            Assert.Contains("explore/code", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.SessionDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_session_passes_include_pack()
    {
        try
        {
            IReadOnlyDictionary<string, JsonElement>? seen = null;
            CitizenRouteHost.SessionDispatchOverride = args =>
            {
                seen = args;
                return """{"plane":"cdp_session","context":{"phase":"act","object":"code"},"pack":{"available":true,"pack_id":"epistemic-scene"}}""";
            };

            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("cdp_session include_pack=true")]);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.True(seen!["include_pack"].GetBoolean());
            Assert.Contains("pack", applied[0].Pulse, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.SessionDispatchOverride = null;
        }
    }

    [Fact]
    public void Execute_session_error_board()
    {
        try
        {
            CitizenRouteHost.SessionDispatchOverride = _ =>
                """{"ok":false,"error":"meta_dispatch_unbound"}""";

            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("session")]);
            Assert.False(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.SessionDispatchOverride = null;
        }
    }
}
