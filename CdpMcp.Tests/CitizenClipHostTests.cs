#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenClipHostTests
{
    [Fact]
    public void Route_copy_text()
    {
        var r = CitizenIntentRouter.RouteOne("copy path=a.cs text=hello");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Clip, r.Verb);
        Assert.Equal("copy", r.Op);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("hello", r.NewString);
    }

    [Fact]
    public void Route_clipboard_and_clear()
    {
        var clip = CitizenIntentRouter.RouteOne("clipboard");
        Assert.True(clip.Ok);
        Assert.Equal("clipboard", clip.Op);

        var clear = CitizenIntentRouter.RouteOne("clipboard_clear");
        Assert.True(clear.Ok);
        Assert.Equal("clipboard_clear", clear.Op);
    }

    [Fact]
    public void Route_paste_place()
    {
        var r = CitizenIntentRouter.RouteOne("paste path=a.cs place=after");
        Assert.True(r.Ok);
        Assert.Equal("paste", r.Op);
        Assert.Equal("a.cs", r.Path);
    }

    [Fact]
    public void Execute_copy_passes_text_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ClipCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"copy","frame":"c1","chars":5,"meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("copy path=a.cs text=hello")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("copy", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("copy", seen!["op"].GetString());
            Assert.Equal("hello", seen["text"].GetString());
            Assert.Contains("c1", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ClipCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_clipboard_empty_surfaces_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ClipCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":true,"op":"clipboard","empty":true,"clipboard":{"count":0}}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("clipboard")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Contains("empty", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ClipCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
