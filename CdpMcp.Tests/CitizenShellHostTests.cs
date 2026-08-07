#nullable enable
using TerminalMcp.Core;
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
        Assert.Null(r.Op);
    }

    [Fact]
    public void Route_shell_command_keyed()
    {
        var r = CitizenIntentRouter.RouteOne("""shell command="dotnet --version" tab=citizen""");
        Assert.True(r.Ok);
        Assert.Equal("dotnet --version", r.Command);
    }

    [Fact]
    public void Route_shell_command_unquoted_keeps_rest()
    {
        var r = CitizenIntentRouter.RouteOne("shell command=echo monday-dod tab=citizen");
        Assert.True(r.Ok);
        Assert.Equal("echo monday-dod", r.Command);
    }

    [Fact]
    public void Route_shell_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("shell");
        Assert.True(r.Ok);
        Assert.Equal("scene", r.Op);
        Assert.Null(r.Command);
    }

    [Fact]
    public void Route_shell_habitat_verbs()
    {
        foreach (var verb in new[] { "scene", "which", "last", "history", "rerun", "kill", "close" })
        {
            var r = CitizenIntentRouter.RouteOne("shell " + verb);
            Assert.True(r.Ok, verb);
            Assert.Equal(verb, r.Op);
            Assert.Null(r.Command);
        }
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

    [Fact]
    public void Execute_shell_scene_with_habitat_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ShellHabitatResolver = () => new ShellHabitat();
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("shell scene")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("shell", applied[0].Action);
            Assert.Equal("scene", applied[0].Cmd);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_shell_which_with_organ_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.ShellOrganOverride = (op, tab) =>
            """{"schema":"shell_which/v0","ok":true,"tab":"main","shell_kind":"pwsh"}""";
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("shell which")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("which", applied[0].Cmd);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
