#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenDeployHostTests
{
    [Fact]
    public void Route_deploy_defaults_hard()
    {
        var r = CitizenIntentRouter.RouteOne("deploy");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Deploy, r.Verb);
        Assert.Equal("hard", r.Op);
        Assert.Equal("deploy", r.Go);
    }

    [Fact]
    public void Route_deploy_mode_soft_target()
    {
        var r = CitizenIntentRouter.RouteOne("deploy mode=soft target=sibling");
        Assert.True(r.Ok);
        Assert.Equal("soft", r.Op);
        Assert.Equal("sibling", r.Detail);
    }

    [Fact]
    public void Route_hard_deploy_alias()
    {
        var r = CitizenIntentRouter.RouteOne("hard_deploy");
        Assert.True(r.Ok);
        Assert.Equal("hard", r.Op);
    }

    [Fact]
    public void Route_deploy_unknown_mode_fails()
    {
        var r = CitizenIntentRouter.RouteOne("deploy mode=explode");
        Assert.False(r.Ok);
        Assert.Equal("deploy_mode_unknown", r.Reason);
    }

    [Fact]
    public void Execute_deploy_dry_run_passes_args_via_override()
    {
        CitizenRouteHost.UnbindLifecycle();
        IReadOnlyDictionary<string, JsonElement>? seen = null;
        CitizenRouteHost.DeployCallOverride = (_, args) =>
        {
            seen = args;
            return """{"schema":"deploy/v0","ok":true,"op":"hard","dry_run":true,"pulse":"deploy · dry_run · hard sibling"}""";
        };
        try
        {
            var applied = CitizenRouteHost.Execute([
                CitizenIntentRouter.RouteOne("deploy mode=hard target=sibling dry_run=true")
            ]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("deploy", applied[0].Action);
            Assert.NotNull(seen);
            Assert.Equal("hard", seen!["mode"].GetString());
            Assert.Equal("sibling", seen["target"].GetString());
            Assert.True(seen["dry_run"].GetBoolean());
            Assert.Contains("deploy", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.DeployCallOverride = null;
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
