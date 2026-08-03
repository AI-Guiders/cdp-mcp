#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenDiskHostTests
{
    [Fact]
    public void Route_reload_bare()
    {
        var r = CitizenIntentRouter.RouteOne("reload");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Disk, r.Verb);
        Assert.Equal("reload", r.Op);
    }

    [Fact]
    public void Route_keep_disk_path()
    {
        var r = CitizenIntentRouter.RouteOne("keep_disk path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal("keep_disk", r.Op);
        Assert.Equal("a.cs", r.Path);
    }

    [Fact]
    public void Route_disk_peek_pad()
    {
        var r = CitizenIntentRouter.RouteOne("disk_peek path=a.cs pad=3");
        Assert.True(r.Ok);
        Assert.Equal("disk_peek", r.Op);
        Assert.Equal("a.cs", r.Path);
        Assert.Equal("3", r.Detail);
    }

    [Fact]
    public void Execute_reload_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.DiskCallOverride = args =>
        {
            seen = args;
            return """{"schema":"doc_reload/v0","ok":true,"op":"reload","count":1,"meta":{"path":"D:\\tmp\\a.cs","doc_id":"doc-1"}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("reload path=a.cs")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("reload", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("reload", seen!["op"].GetString());
            Assert.Equal("a.cs", seen["path"].GetString());
            Assert.Contains("n=1", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.DiskCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_disk_peek_passes_pad_as_number()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.DiskCallOverride = args =>
        {
            seen = args;
            return """{"schema":"doc_disk_peek_batch/v0","ok":true,"op":"disk_peek","count":0}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("disk_peek pad=3")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal(JsonValueKind.Number, seen!["pad"].ValueKind);
            Assert.Equal(3, seen["pad"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.DiskCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_disk_peek_surfaces_error()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.DiskCallOverride = _ =>
            """{"schema":"doc_disk_peek/v0","ok":false,"op":"disk_peek","error":"not_open"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("disk_peek path=missing.cs")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("not_open", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.DiskCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
