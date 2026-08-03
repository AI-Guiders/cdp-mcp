#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenWebcamHostTests
{
    [Fact]
    public void Route_webcam_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("webcam");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Webcam, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("webcam_desk", r.Go);
    }

    [Fact]
    public void Route_aliases_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("webcam_desk");
        Assert.True(desk.Ok);
        Assert.Equal("scene", desk.Op);

        var frame = CitizenIntentRouter.RouteOne("webcam_frame");
        Assert.True(frame.Ok);
        Assert.Equal("frame", frame.Op);

        var window = CitizenIntentRouter.RouteOne("webcam window_list");
        Assert.True(window.Ok);
        Assert.Equal("window_list", window.Op);

        var windowsAlias = CitizenIntentRouter.RouteOne("webcam windows");
        Assert.True(windowsAlias.Ok);
        Assert.Equal("window_list", windowsAlias.Op);

        var compoundList = CitizenIntentRouter.RouteOne("webcam_window_list");
        Assert.True(compoundList.Ok);
        Assert.Equal("window_list", compoundList.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_webcam op=scene");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);
    }

    [Fact]
    public void Route_no_steal_bare_frame_or_screen()
    {
        var bareFrame = CitizenIntentRouter.RouteOne("frame");
        Assert.NotEqual(CitizenIntentRouter.Verb.Webcam, bareFrame.Verb);

        var bareScreen = CitizenIntentRouter.RouteOne("screen");
        Assert.NotEqual(CitizenIntentRouter.Verb.Webcam, bareScreen.Verb);

        var bareOcr = CitizenIntentRouter.RouteOne("ocr");
        Assert.NotEqual(CitizenIntentRouter.Verb.Webcam, bareOcr.Verb);
    }

    [Fact]
    public void Route_webcam_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("webcam boom");
        Assert.False(r.Ok);
        Assert.Equal("webcam_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.WebcamHandleOverride = (_, _) =>
            new { schema = "webcam/v0", ok = true, op = "scene", pulse = "webcam · scene · ready" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("webcam")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("webcam", applied[0].Action);
            Assert.Equal("webcam_desk", applied[0].Go);
            Assert.Contains("webcam", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.WebcamHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_window_passes_op()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.WebcamHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "webcam/v0", ok = true, op = "window", pulse = "webcam · window" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("webcam window")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("window", seen!["op"].GetString());
        }
        finally
        {
            CitizenRouteHost.WebcamHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_window_list_passes_op()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.WebcamHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "webcam/v0", ok = true, op = "window_list", pulse = "webcam · window_list" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("webcam window_list")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("window_list", seen!["op"].GetString());
        }
        finally
        {
            CitizenRouteHost.WebcamHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
