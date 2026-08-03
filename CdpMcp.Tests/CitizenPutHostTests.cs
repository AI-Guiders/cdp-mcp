#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenPutHostTests
{
    [Fact]
    public void Route_put_path_text()
    {
        var r = CitizenIntentRouter.RouteOne("put path=a.txt text=hello");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Put, r.Verb);
        Assert.Equal("put", r.Op);
        Assert.Equal("a.txt", r.Path);
        Assert.Equal("hello", r.NewString);
    }

    [Fact]
    public void Route_put_requires_path_or_dest()
    {
        var r = CitizenIntentRouter.RouteOne("put text=hello");
        Assert.False(r.Ok);
        Assert.Contains("path_or_dest", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Route_put_requires_body_or_frame()
    {
        var r = CitizenIntentRouter.RouteOne("put path=a.txt");
        Assert.False(r.Ok);
        Assert.Contains("body_or_frame", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Route_put_before_create_write()
    {
        var r = CitizenIntentRouter.RouteOne("put path=a.txt text=x");
        Assert.Equal(CitizenIntentRouter.Verb.Put, r.Verb);
    }

    [Fact]
    public void Execute_put_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.PutCallOverride = args =>
        {
            seen = args;
            return """{"schema":"editor_comfort/v0","ok":true,"op":"put","mode":"create","chars":5,"meta":{"path":"D:\\tmp\\a.txt","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("put path=a.txt text=hello")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("put", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("put", seen!["op"].GetString());
            Assert.Equal("hello", seen["text"].GetString());
            Assert.Contains("create", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("chars=5", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.PutCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_put_surfaces_file_exists()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.PutCallOverride = _ =>
            """{"schema":"editor_comfort/v0","ok":false,"op":"put","error":"file_exists"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("put path=a.txt text=x")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("file_exists", applied[0].Reason, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.PutCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
