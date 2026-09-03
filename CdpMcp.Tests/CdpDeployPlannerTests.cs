using Cdp.Deploy;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpDeployPlannerTests
{
    [Fact]
    public void Layout_rejects_colliding_service_and_bridge_roots()
    {
        var bad = new CdpDeployLayout(
            ServiceInstall: @"D:\cdp-service",
            BridgeReleaseInstall: @"D:\cdp-service",
            BridgeDebugInstall: @"D:\cdp-mcp-debug");

        var ex = Assert.Throws<InvalidOperationException>(() => bad.ValidateDistinctRoots());
        Assert.Contains("distinct", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveBridgeTarget_rejects_service_path_as_bridge_target()
    {
        var result = CdpDeployPlanner.PlanInstallTarget(
            CdpDeployInstallRequest.ForResolve(
                CdpDeployMode.Soft,
                selfInstallRoot: @"D:\cdp-mcp",
                targetRaw: @"D:\cdp-service",
                force: false));

        Assert.False(result.Ok);
        Assert.Equal("target_unresolved", result.Error);
    }

    [Fact]
    public void Soft_plan_stages_service_and_bridge_on_distinct_next_trees()
    {
        var source = CdpDeploySource.TryResolve(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Assert.NotNull(source);

        var result = CdpDeployPlanner.Plan(new CdpDeployPlanRequest(
            CdpDeployMode.Soft,
            SelfInstallRoot: CdpDeployLayout.Default.BridgeReleaseInstall,
            RepoSearchRoot: source!.RepoRoot,
            TargetRaw: "sibling",
            Force: false,
            UseNuGet: false,
            NoNudge: true,
            Source: source));

        Assert.True(result.Ok, result.Hint);
        var plan = result.Plan!;
        Assert.Equal(@"D:\cdp-service.next", plan.ServicePublishRoot);
        Assert.Equal(@"D:\cdp-mcp-debug.next", plan.BridgePublishRoot);
        Assert.NotEqual(plan.ServicePublishRoot, plan.BridgePublishRoot);
    }

    [Fact]
    public void Hard_self_refused_without_force()
    {
        var result = CdpDeployPlanner.PlanInstallTarget(
            CdpDeployInstallRequest.ForResolve(
                CdpDeployMode.Hard,
                selfInstallRoot: CdpDeployLayout.Default.BridgeDebugInstall,
                targetRaw: "self",
                force: false));

        Assert.False(result.Ok);
        Assert.Equal("refuse_hard_self", result.Error);
    }

    [Fact]
    public void AidPublish_BuildCommand_prefers_global_tool_exe()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var (fileName, _) = CdpAidPublishRunner.BuildCommand(new CdpAidPublishRequest(
            Path.Combine(repo, "CdpMcp.csproj"),
            @"D:\cdp-service.next",
            KillRunning: false,
            UseNuGet: false,
            PreserveConfigToml: null,
            WorkingDirectory: repo));

        Assert.Contains("aid-publish", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("dotnet", fileName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AidPublish_finds_tool_when_path_empty()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            var (fileName, _) = CdpAidPublishRunner.BuildCommand(new CdpAidPublishRequest(
                Path.Combine(repo, "CdpMcp.csproj"),
                @"D:\cdp-service.next",
                KillRunning: false,
                UseNuGet: false,
                PreserveConfigToml: null,
                WorkingDirectory: repo));
            Assert.Contains("aid-publish.exe", fileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
        }
    }

    [Fact]
    public void Apply_plan_does_not_require_repo_source()
    {
        var result = CdpDeployPlanner.Plan(new CdpDeployPlanRequest(
            CdpDeployMode.Apply,
            SelfInstallRoot: CdpDeployLayout.Default.ServiceInstall,
            RepoSearchRoot: null,
            TargetRaw: "service",
            Force: false,
            UseNuGet: false,
            NoNudge: true));

        Assert.True(result.Ok, result.Hint);
        Assert.Equal(CdpDeployMode.Apply, result.Plan!.Mode);
    }

    [Fact]
    public void Soft_orchestrator_stages_distinct_next_trees()
    {
        var source = CdpDeploySource.TryResolve(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")));
        Assert.NotNull(source);

        var planResult = CdpDeployPlanner.Plan(new CdpDeployPlanRequest(
            CdpDeployMode.Soft,
            SelfInstallRoot: CdpDeployLayout.Default.BridgeReleaseInstall,
            RepoSearchRoot: source!.RepoRoot,
            TargetRaw: "sibling",
            Force: false,
            UseNuGet: false,
            NoNudge: true,
            Source: source));

        Assert.True(planResult.Ok, planResult.Hint);
        var step = CdpDeployOrchestrator.Run(planResult.Plan!);
        Assert.True(step.Ok, step.Stderr ?? step.Pulse);
    }
}
