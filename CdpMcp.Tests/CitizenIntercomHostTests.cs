#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenIntercomHostTests
{
    [Fact]
    public void Route_intercom_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("intercom");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Intercom, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("intercom", r.Go);
    }

    [Fact]
    public void Route_send_requires_body_and_presence_requires_state()
    {
        var bare = CitizenIntentRouter.RouteOne("intercom send");
        Assert.False(bare.Ok);
        Assert.Equal("intercom_body_required", bare.Reason);

        var send = CitizenIntentRouter.RouteOne("intercom send to=pm body=hello");
        Assert.True(send.Ok);
        Assert.Equal("send", send.Op);

        var noState = CitizenIntentRouter.RouteOne("intercom presence seat=pf");
        Assert.False(noState.Ok);
        Assert.Equal("intercom_state_required", noState.Reason);

        var presence = CitizenIntentRouter.RouteOne("intercom presence seat=pf state=busy");
        Assert.True(presence.Ok);
        Assert.Equal("presence", presence.Op);
    }

    [Fact]
    public void Route_compounds_and_no_steal_bare_send()
    {
        var compound = CitizenIntentRouter.RouteOne("intercom_send body=hi");
        Assert.True(compound.Ok);
        Assert.Equal("send", compound.Op);

        var hist = CitizenIntentRouter.RouteOne("intercom history limit=5");
        Assert.True(hist.Ok);
        Assert.Equal("history", hist.Op);
        Assert.Equal("5", hist.Detail);

        // bare send stays Unknown / not Intercom
        var bare = CitizenIntentRouter.RouteOne("send body=nope");
        Assert.NotEqual(CitizenIntentRouter.Verb.Intercom, bare.Verb);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.IntercomHandleOverride = _ =>
            """{"ok":true,"op":"scene","pulse":"intercom scene ok"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("intercom")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("intercom", applied[0].Action);
            Assert.Contains("intercom", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.IntercomHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_send_passes_body_and_to()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.IntercomHandleOverride = args =>
        {
            seen = args;
            return """{"ok":true,"op":"send","chat":"@PM: peer"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("intercom send to=pm body=\"peer dogfood\"")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("send", seen!["op"].GetString());
            Assert.Equal("pm", seen["to"].GetString());
            Assert.Equal("peer dogfood", seen["body"].GetString());
        }
        finally
        {
            CitizenRouteHost.IntercomHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
