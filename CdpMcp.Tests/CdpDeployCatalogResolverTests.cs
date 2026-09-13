using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpDeployCatalogResolverTests
{
    static readonly CdpDeployCatalogResolver Resolver = CdpDeployCatalogResolver.Default;

    [Theory]
    [InlineData("deploy", "deploy", CdpDeployMode.Ship)]
    [InlineData("hard_deploy", "hard_deploy", CdpDeployMode.Hard)]
    [InlineData("soft_deploy", "soft_deploy", CdpDeployMode.Soft)]
    public void Bare_head_sets_go_and_default_mode(string head, string go, CdpDeployMode mode)
    {
        Assert.True(Resolver.TryParse(head, [head], out var cmd));
        Assert.Equal(go, cmd.Go);
        Assert.Equal(mode, cmd.Mode);
        Assert.Null(cmd.Target);
        Assert.False(cmd.DryRun);
    }

    [Fact]
    public void Deploy_dry_sibling_parses_ship_target_and_dry_run()
    {
        Assert.True(Resolver.TryParse("deploy", ["deploy", "dry", "sibling"], out var cmd));
        Assert.Equal("deploy", cmd.Go);
        Assert.Equal(CdpDeployMode.Ship, cmd.Mode);
        Assert.Equal("sibling", cmd.Target);
        Assert.True(cmd.DryRun);
    }

    [Fact]
    public void Target_keyed_and_mode_override()
    {
        Assert.True(Resolver.TryParse("deploy", ["deploy", "hard", "target=debug", "peek"], out var cmd));
        Assert.Equal(CdpDeployMode.Hard, cmd.Mode);
        Assert.Equal("debug", cmd.Target);
        Assert.True(cmd.DryRun);
    }

    [Fact]
    public void Target_keyed_value_splits_on_first_equals_only()
    {
        Assert.True(Resolver.TryParse("deploy", ["deploy", "target=path=with=equals"], out var cmd));
        Assert.Equal("path=with=equals", cmd.Target);
    }

    [Fact]
    public void Unknown_head_is_not_handled()
    {
        Assert.False(Resolver.TryParse("ship_git", ["ship_git"], out _));
    }

    [Fact]
    public void TryParseLine_citizen_kv_deploy()
    {
        Assert.True(Resolver.TryParseLine("deploy mode=soft target=sibling", out var cmd));
        Assert.Equal("deploy", cmd.Go);
        Assert.Equal(CdpDeployMode.Soft, cmd.Mode);
        Assert.Equal("sibling", cmd.Target);
    }

    [Fact]
    public void Hard_deploy_head_via_line()
    {
        Assert.True(Resolver.TryParseLine("hard_deploy", out var cmd));
        Assert.Equal("hard_deploy", cmd.Go);
        Assert.Equal(CdpDeployMode.Hard, cmd.Mode);
    }
}
