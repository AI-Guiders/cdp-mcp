#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("CitizenRouteHostLifecycle")]
public sealed class CitizenGitHostTests
{
    [Fact]
    public void Route_git_alone_is_scene()
    {
        var r = CitizenIntentRouter.RouteOne("git");
        Assert.True(r.Ok);
        Assert.Equal(CitizenIntentRouter.Verb.Git, r.Verb);
        Assert.Equal("scene", r.Op);
        Assert.Equal("git", r.Go);
    }

    [Fact]
    public void Route_git_status_aliases_scene()
    {
        var r = CitizenIntentRouter.RouteOne("git status");
        Assert.True(r.Ok);
        Assert.Equal("scene", r.Op);
    }

    [Fact]
    public void Route_git_show_requires_rev()
    {
        var r = CitizenIntentRouter.RouteOne("git show");
        Assert.False(r.Ok);
        Assert.Equal("git_rev_required", r.Reason);
    }

    [Fact]
    public void Route_git_commit_requires_message()
    {
        var r = CitizenIntentRouter.RouteOne("git commit");
        Assert.False(r.Ok);
        Assert.Equal("git_message_required", r.Reason);
    }

    [Fact]
    public void Route_git_commit_with_message_ok()
    {
        var r = CitizenIntentRouter.RouteOne("git commit message=\"feat: peer scm\"");
        Assert.True(r.Ok);
        Assert.Equal("commit", r.Op);
    }

    [Fact]
    public void Route_git_push_ok()
    {
        var r = CitizenIntentRouter.RouteOne("git push");
        Assert.True(r.Ok);
        Assert.Equal("push", r.Op);
    }

    [Fact]
    public void Route_git_branch_unknown()
    {
        var r = CitizenIntentRouter.RouteOne("git branch");
        Assert.False(r.Ok);
        Assert.Equal("git_tool_unknown", r.Reason);
    }

    [Fact]
    public void Execute_git_without_backend_fails_disabled()
    {
        CitizenRouteHost.UnbindLifecycle();
        var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("git")]);
        Assert.Single(applied);
        Assert.False(applied[0].Ok);
        Assert.Equal("git", applied[0].Action);
        Assert.Equal("git_disabled", applied[0].Reason);
    }

    [Fact]
    public void Execute_git_scene_with_override_ok()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.GitCallOverride = (tool, _) =>
        {
            Assert.Equal("git_scene", tool);
            return Task.FromResult("""{"ok":true,"schema":"git_scene/v0","roots":[{"path":"/r","ok":true,"branch":"main","dirty":false,"counts":{"staged":0,"unstaged":0,"untracked":0}}]}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute([CitizenIntentRouter.RouteOne("git")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
            Assert.Equal("git", applied[0].Action);
            Assert.Contains("clean", applied[0].Pulse, StringComparison.Ordinal);
            Assert.Contains("main", applied[0].Pulse, StringComparison.Ordinal);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }

    [Fact]
    public void Execute_git_commit_passes_paths_array()
    {
        CitizenRouteHost.UnbindLifecycle();
        CitizenRouteHost.GitCallOverride = (tool, args) =>
        {
            Assert.Equal("git_commit", tool);
            Assert.True(args.TryGetValue("paths", out var paths));
            Assert.Equal(JsonValueKind.Array, paths.ValueKind);
            Assert.Equal(1, paths.GetArrayLength());
            Assert.Equal("CitizenRouteHost.Git.cs", paths[0].GetString());
            return Task.FromResult("""{"ok":true,"exit_code":0}""");
        };
        try
        {
            var applied = CitizenRouteHost.Execute(
                [CitizenIntentRouter.RouteOne(
                    "git commit message=\"feat: scoped\" paths=[\"CitizenRouteHost.Git.cs\"]")]);
            Assert.Single(applied);
            Assert.True(applied[0].Ok);
        }
        finally
        {
            CitizenRouteHost.UnbindLifecycle();
        }
    }
}
