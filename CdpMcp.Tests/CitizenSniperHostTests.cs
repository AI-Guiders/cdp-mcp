#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenSniperHostTests
{
    [Fact]
    public void Route_scope_from_wire()
    {
        var r = CitizenIntentRouter.RouteOne("scope from=[F:a.cs;L:10]");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Sniper, r.Verb);
        Assert.Equal("scope", r.Op);
        Assert.Equal("[F:a.cs;L:10]", r.OldString);
    }

    [Fact]
    public void Route_peek_bare()
    {
        var r = CitizenIntentRouter.RouteOne("peek");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Sniper, r.Verb);
        Assert.Equal("peek", r.Op);
    }

    [Fact]
    public void Route_disk_peek_still_disk()
    {
        var r = CitizenIntentRouter.RouteOne("disk_peek path=a.cs");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Disk, r.Verb);
        Assert.Equal("disk_peek", r.Op);
    }

    [Fact]
    public void Route_scope_clear()
    {
        var r = CitizenIntentRouter.RouteOne("scope_clear");
        Assert.True(r.Ok);
        Assert.Equal("clear", r.Op);
    }

    [Fact]
    public void Route_sniper_status()
    {
        var r = CitizenIntentRouter.RouteOne("sniper");
        Assert.True(r.Ok);
        Assert.Equal("status", r.Op);
    }

    [Fact]
    public void Execute_scope_passes_from_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.SniperCallOverride = args =>
        {
            seen = args;
            return """{"schema":"edit_sniper/v0","ok":true,"op":"scope","phase":"armed","hold":{"phase":"armed","line_start":10,"line_end":12}}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("scope from=[F:a.cs;L:10]")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("scope", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("scope", seen!["op"].GetString());
            Assert.Equal("[F:a.cs;L:10]", seen["from"].GetString());
            Assert.Contains("armed", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.SniperCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_scope_surfaces_error()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.SniperCallOverride = _ =>
            """{"schema":"edit_sniper/v0","ok":false,"op":"scope","error":"anchor_unresolved"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("scope from=[F:missing.cs;L:1]")
            ]);
            Assert.Single(applied);
            Assert.False(applied[0].Ok);
            Assert.Contains("anchor_unresolved", applied[0].Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenRouteHost.SniperCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
