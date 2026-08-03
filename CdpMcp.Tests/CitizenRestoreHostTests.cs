#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenRestoreHostTests
{
    [Fact]
    public void Route_restore_alone_is_restore()
    {
        var r = CitizenIntentRouter.RouteOne("restore");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Restore, r.Verb);
        Assert.Equal("restore", r.Op);
        Assert.Equal("restore", r.Organ);
        Assert.Equal("restore", r.Go);
    }

    [Fact]
    public void Route_recent_alone_is_list()
    {
        var r = CitizenIntentRouter.RouteOne("recent");
        Assert.True(r.Ok);
        Assert.Equal("list", r.Op);
        Assert.Equal("recent", r.Organ);
        Assert.Equal("recent", r.Go);
    }

    [Fact]
    public void Route_peek_and_take()
    {
        var peek = CitizenIntentRouter.RouteOne("restore peek");
        Assert.True(peek.Ok);
        Assert.Equal("peek", peek.Op);

        var take = CitizenIntentRouter.RouteOne("recent take=5");
        Assert.True(take.Ok);
        Assert.Equal("list", take.Op);
        Assert.Equal("5", take.Detail);
    }

    [Fact]
    public void Route_unknown_compounds_and_no_steal_land_or_recent_files()
    {
        var bad = CitizenIntentRouter.RouteOne("restore boom");
        Assert.False(bad.Ok);
        Assert.Equal("restore_op_unknown", bad.Reason);

        var compound = CitizenIntentRouter.RouteOne("restore_peek");
        Assert.True(compound.Ok);
        Assert.Equal("peek", compound.Op);

        // land restore stays Land
        var land = CitizenIntentRouter.RouteOne("land restore");
        Assert.True(land.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Land, land.Verb);

        // recent_files stays Nav
        var nav = CitizenIntentRouter.RouteOne("recent_files");
        Assert.True(nav.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Nav, nav.Verb);
    }

    [Fact]
    public void Execute_restore_peek_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        string? seenTool = null;
        CitizenRouteHost.RestoreDispatchOverride = (tool, _) =>
        {
            seenTool = tool;
            return """{"ok":true,"op":"peek","buffer_count":2,"pulse":"restore peek ok"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("restore peek")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("restore", applied[0].Action);
            Assert.Equal("cdp_restore", seenTool);
            Assert.Contains("restore", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.RestoreDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_recent_passes_take()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        string? tool = null;
        CitizenRouteHost.RestoreDispatchOverride = (t, args) =>
        {
            tool = t;
            seen = args;
            return """{"count":5,"items":[]}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("recent take=5")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("cdp_recent", tool);
            Assert.NotNull(seen);
            Assert.Equal(5, seen!["take"].GetInt32());
        }
        finally
        {
            CitizenRouteHost.RestoreDispatchOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
