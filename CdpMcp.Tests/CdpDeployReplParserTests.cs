using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpDeployReplParserTests
{
    static readonly CdpDeployReplParser Parser = CdpDeployReplParser.Default;

    [Theory]
    [InlineData("deploy", "deploy", CdpDeployMode.Ship)]
    [InlineData("hard_deploy", "hard_deploy", CdpDeployMode.Hard)]
    [InlineData("soft_deploy", "soft_deploy", CdpDeployMode.Soft)]
    public void Bare_head_sets_go_and_default_mode(string head, string go, CdpDeployMode mode)
    {
        Assert.True(Parser.TryParse(head, [head], out var cmd));
        Assert.Equal(go, cmd.Go);
        Assert.Equal(mode, cmd.Mode);
        Assert.Null(cmd.Target);
        Assert.False(cmd.DryRun);
    }

    [Fact]
    public void Deploy_dry_sibling_parses_ship_target_and_dry_run()
    {
        Assert.True(Parser.TryParse("deploy", ["deploy", "dry", "sibling"], out var cmd));
        Assert.Equal("deploy", cmd.Go);
        Assert.Equal(CdpDeployMode.Ship, cmd.Mode);
        Assert.Equal("sibling", cmd.Target);
        Assert.True(cmd.DryRun);
    }

    [Fact]
    public void Target_keyed_and_mode_override()
    {
        Assert.True(Parser.TryParse("deploy", ["deploy", "hard", "target=debug", "peek"], out var cmd));
        Assert.Equal(CdpDeployMode.Hard, cmd.Mode);
        Assert.Equal("debug", cmd.Target);
        Assert.True(cmd.DryRun);
    }

    [Fact]
    public void Unknown_head_is_not_handled()
    {
        Assert.False(Parser.TryParse("ship_git", ["ship_git"], out _));
    }
}
