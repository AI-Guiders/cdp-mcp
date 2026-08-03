#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenArchHostTests
{
    [Fact]
    public void Route_arch_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("arch");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Arch, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("arch_desk", r.Go);
    }

    [Fact]
    public void Route_desk_board_cdp_and_compounds()
    {
        var desk = CitizenIntentRouter.RouteOne("arch_desk");
        Assert.True(desk.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Arch, desk.Verb);
        Assert.Equal("scene", desk.Op);

        var board = CitizenIntentRouter.RouteOne("board");
        Assert.True(board.Ok);
        Assert.Equal("scene", board.Op);

        var cdp = CitizenIntentRouter.RouteOne("cdp_arch");
        Assert.True(cdp.Ok);
        Assert.Equal("scene", cdp.Op);

        var roles = CitizenIntentRouter.RouteOne("arch_roles");
        Assert.True(roles.Ok);
        Assert.Equal("roles", roles.Op);

        var asBuilt = CitizenIntentRouter.RouteOne("arch as_built profile=cdp_desk");
        Assert.True(asBuilt.Ok);
        Assert.Equal("as_built", asBuilt.Op);
        Assert.Equal("cdp_desk", asBuilt.Path);

        var addRole = CitizenIntentRouter.RouteOne("arch add_role role=ccu");
        Assert.True(addRole.Ok);
        Assert.Equal("add_role", addRole.Op);
        Assert.Equal("ccu", addRole.Path);
    }

    [Fact]
    public void Route_no_steal_bare_scene_roles_clear()
    {
        Assert.NotEqual(CitizenIntentRouter.Verb.Arch, CitizenIntentRouter.RouteOne("scene").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Arch, CitizenIntentRouter.RouteOne("roles").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Arch, CitizenIntentRouter.RouteOne("clear").Verb);
        Assert.NotEqual(CitizenIntentRouter.Verb.Arch, CitizenIntentRouter.RouteOne("promote").Verb);
    }

    [Fact]
    public void Route_arch_unknown_op_fails()
    {
        var r = CitizenIntentRouter.RouteOne("arch boom");
        Assert.False(r.Ok);
        Assert.Equal("arch_op_unknown", r.Reason);
    }

    [Fact]
    public void Execute_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ArchHandleOverride = (_, _) =>
            new { schema = "arch_board/v0", ok = true, op = "scene", pulse = "arch_board · 10 roles" };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("arch")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("arch", applied[0].Action);
            Assert.Contains("arch", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.ArchHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_add_role_passes_role()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.ArchHandleOverride = (_, args) =>
        {
            seen = args;
            return new { schema = "arch_board/v0", ok = true, op = "add_role", pulse = "arch_board · +ccu" };
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("arch add_role role=ccu id=plan-only")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.NotNull(seen);
            Assert.Equal("add_role", seen!["op"].GetString());
            Assert.Equal("ccu", seen["role"].GetString());
            Assert.Equal("plan-only", seen["id"].GetString());
        }
        finally
        {
            CitizenRouteHost.ArchHandleOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
