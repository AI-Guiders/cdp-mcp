#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenBufferHostTests
{
    [Fact]
    public void Route_read_path_window()
    {
        var r = CitizenIntentRouter.RouteOne("read path=a.cs start_line=1 end_line=3");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Buffer, r.Verb);
        Assert.Equal("read", r.Op);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("1", r.Detail);
        Assert.Equal("3", r.NewString);
    }

    [Fact]
    public void Route_close_path()
    {
        var r = CitizenIntentRouter.RouteOne("close path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal("close", r.Op);
        Assert.Equal("a.cs", r.Path);
    }

    [Fact]
    public void Route_buffers_scene()
    {
        var r = CitizenIntentRouter.RouteOne("buffers");
        Assert.True(r.Ok);
        Assert.Equal("scene", r.Op);
    }

    [Fact]
    public void Route_doc_diagnostics_not_ide()
    {
        var r = CitizenIntentRouter.RouteOne("doc_diagnostics path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Buffer, r.Verb);
        Assert.Equal("diagnostics", r.Op);
    }

    [Fact]
    public void Route_bare_diagnostics_still_ide()
    {
        var r = CitizenIntentRouter.RouteOne("diagnostics path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Ide, r.Verb);
    }

    [Fact]
    public void Execute_read_passes_window_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.BufferCallOverride = args =>
        {
            seen = args;
            return """{"schema":"doc_read/v0","meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1","line_count":3},"start_line":1,"end_line":3,"text":"x"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("read path=a.cs start_line=1 end_line=3")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("read", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("read", seen!["op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Equal(1, seen["start_line"].GetInt32());
            Assert.Equal(3, seen["end_line"].GetInt32());
            Assert.Contains("L1-3", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.BufferCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_close_surfaces_error()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.BufferCallOverride = _ =>
            """{"schema":"doc_close/v0","ok":false,"op":"close","error":"not_open"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("close path=missing.cs")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("not_open", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.BufferCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_buffers_scene_ok_without_ok_flag()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.BufferCallOverride = _ =>
            """{"schema":"doc_scene/v0","count":2,"docs":[]}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("buffers")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("scene", applied[0].Action);
            Assert.Contains("n=2", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.BufferCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
