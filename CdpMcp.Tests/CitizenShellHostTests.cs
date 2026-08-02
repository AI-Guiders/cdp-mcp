#nullable enable
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenShellHostTests
{
    [Fact]
    public void Route_shell_rest_is_command()
    {
        var r = CitizenIntentRouter.RouteOne("shell echo hi");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Shell, r.Verb);
        Assert.Equal("echo hi", r.Command);
        Assert.Equal("shell", r.Go);
    }

    [Fact]
    public void Route_shell_command_keyed()
    {
        var r = CitizenIntentRouter.RouteOne("""shell command="dotnet --version" tab=citizen""");
        Assert.True(r.Ok);
        Assert.Equal("dotnet --version", r.Command);
    }

    [Fact]
    public void Route_shell_alone_requires_command()
    {
        var r = CitizenIntentRouter.RouteOne("shell");
        Assert.False(r.Ok);
        Assert.Equal("shell_command_required", r.Reason);
    }

    [Fact]
    public void Execute_shell_without_habitat_fails_no_shell()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("shell echo x")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("no_shell", applied[0].Reason);
    }

    [Fact]
    public void Execute_shell_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ShellRunOverride = (cmd, tab, cwd) =>
            """{"schema":"shell_run/v0","ok":true,"exit_code":0,"tab":"main"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("shell echo ok")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("shell", applied[0].Action);
            Assert.Contains("shell ok", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
