using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cdp.Deploy;

public sealed record CdpDeployStepResult(bool Ok, string Pulse, string? OkLine, int ExitCode, string? Stderr);

public static class CdpDeployOrchestrator
{
    public static CdpDeployStepResult Run(CdpDeployPlan plan)
    {
        plan.Layout.ValidateDistinctRoots();
        return plan.Mode switch
        {
            CdpDeployMode.Soft => Soft(plan),
            CdpDeployMode.Hard => Hard(plan),
            CdpDeployMode.Apply => Apply(plan),
            _ => new CdpDeployStepResult(false, "unsupported mode", null, 1, plan.Mode.ToString())
        };
    }

    static CdpDeployStepResult Soft(CdpDeployPlan plan)
    {
        PublishService(plan, killRunning: false);
        PublishBridgeSeat(plan, plan.BridgePublishRoot);
        var debugRoot = plan.BridgeDebugPublishRoot;
        if (debugRoot is not null && !CdpDeployPaths.SamePath(plan.BridgePublishRoot, debugRoot))
            PublishBridgeSeat(plan, debugRoot);

        var serviceExe = Path.Combine(plan.ServicePublishRoot, "CdpService.exe");
        string? version = null;
        try
        {
            version = FileVersionInfo.GetVersionInfo(serviceExe).FileVersion;
        }
        catch
        {
            /* optional */
        }

        CdpDeployPending.WriteSoft(plan.Layout, plan.ServicePublishRoot, plan.BridgePublishRoot, version);
        return new CdpDeployStepResult(
            true,
            $"soft staged service={plan.ServicePublishRoot} bridge={plan.BridgePublishRoot}",
            $"SOFT staged {plan.ServicePublishRoot}",
            0,
            null);
    }

    static CdpDeployStepResult Hard(CdpDeployPlan plan)
    {
        PublishService(plan, killRunning: true);
        PublishBridgeSeat(plan, plan.BridgePublishRoot);
        var debugRoot = plan.BridgeDebugPublishRoot;
        if (debugRoot is not null && !CdpDeployPaths.SamePath(plan.BridgePublishRoot, debugRoot))
            PublishBridgeSeat(plan, debugRoot);

        CdpDeployPending.Clear(plan.Layout);
        CdpServiceControl.StartService(plan.Layout);
        CdpServiceControl.AssertHealthy(plan.Layout);
        if (!plan.NoNudge)
            CdpReloadNudge.TryBumpSeats("cdp", "cdp-debug");

        return new CdpDeployStepResult(
            true,
            $"hard deployed service={plan.Layout.ServiceInstall}",
            $"HARD deployed {plan.Layout.ServiceInstall}",
            0,
            null);
    }

    static CdpDeployStepResult Apply(CdpDeployPlan plan)
    {
        var pending = CdpDeployPending.ReadRequired(plan.Layout);
        var serviceNext = string.IsNullOrWhiteSpace(pending.ServiceRoot)
            ? plan.Layout.StagedService
            : pending.ServiceRoot;

        CdpServiceControl.StopLockHoldersUnder(plan.Layout.ServiceInstall);
        CdpServiceControl.StopLockHoldersUnder(plan.Layout.BridgeReleaseInstall);
        CdpServiceControl.StopLockHoldersUnder(plan.Layout.BridgeDebugInstall);

        CdpDeployPromoter.PromoteTree(serviceNext, plan.Layout.ServiceInstall);
        CdpServiceControl.EnsureServiceExecutable(plan.Layout);

        PromoteBridgeIfStaged(pending.BridgeRoot, plan.Layout.BridgeReleaseInstall);
        PromoteBridgeIfStaged(plan.Layout.StagedBridgeRelease, plan.Layout.BridgeReleaseInstall);
        PromoteBridgeIfStaged(plan.Layout.StagedBridgeDebug, plan.Layout.BridgeDebugInstall);

        CdpDeployPending.Clear(plan.Layout);
        CleanupStaged(plan.Layout.StagedService);
        CleanupStaged(plan.Layout.StagedBridgeRelease);
        CleanupStaged(plan.Layout.StagedBridgeDebug);

        CdpServiceControl.StartService(plan.Layout);
        CdpServiceControl.AssertHealthy(plan.Layout);
        if (!plan.NoNudge)
            CdpReloadNudge.TryBumpSeats("cdp", "cdp-debug");

        return new CdpDeployStepResult(
            true,
            $"apply ok service={plan.Layout.ServiceInstall}",
            $"APPLY ok {plan.Layout.ServiceInstall}",
            0,
            null);
    }

    static void PromoteBridgeIfStaged(string? staged, string live)
    {
        if (string.IsNullOrWhiteSpace(staged) || !Directory.Exists(staged))
            return;

        var resolvedLive = CdpDeployPaths.ResolveLiveFromStaged(staged, live);
        if (CdpDeployPaths.SamePath(resolvedLive, CdpDeployLayout.Default.ServiceInstall))
            return;

        CdpDeployPromoter.PromoteTree(staged, resolvedLive);
    }

    static void CleanupStaged(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    static void PublishService(CdpDeployPlan plan, bool killRunning)
    {
        var result = CdpAidPublishRunner.Publish(new CdpAidPublishRequest(
            plan.Source.ServiceProject,
            plan.ServicePublishRoot,
            killRunning,
            plan.UseNuGet,
            plan.Source.PreserveConfigToml,
            plan.Source.RepoRoot));

        if (result.ExitCode != 0)
        {
            var cmd = CdpAidPublishRunner.BuildCommand(new CdpAidPublishRequest(
                plan.Source.ServiceProject,
                plan.ServicePublishRoot,
                killRunning,
                plan.UseNuGet,
                plan.Source.PreserveConfigToml,
                plan.Source.RepoRoot));
            throw new InvalidOperationException(
                $"CdpService publish failed exit={result.ExitCode} via {cmd.FileName}: {result.Stderr}\nstdout_tail={Tail(result.Stdout, 1500)}");
        }

        var srcExe = Path.Combine(plan.ServicePublishRoot, "CdpMcp.exe");
        var dstExe = Path.Combine(plan.ServicePublishRoot, "CdpService.exe");
        if (File.Exists(srcExe))
            File.Copy(srcExe, dstExe, true);

        CopyTsWorker(plan.Source.RepoRoot, plan.ServicePublishRoot);
        EnsureConfig(plan);
    }

    static string Tail(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return "…" + text[^max..];
    }

    static void PublishBridgeSeat(CdpDeployPlan plan, string bridgeRoot)
    {
        Directory.CreateDirectory(bridgeRoot);
        var result = CdpAidPublishRunner.Publish(new CdpAidPublishRequest(
            plan.Source.BridgeProject,
            bridgeRoot,
            KillRunning: false,
            plan.UseNuGet,
            PreserveConfigToml: null,
            plan.Source.RepoRoot));

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"CdpMcpBridge publish failed exit={result.ExitCode}: {result.Stderr}");

        EnsureConfigAt(bridgeRoot, Path.Combine(plan.Layout.ServiceInstall, "cdp-mcp.toml"));
    }

    static void EnsureConfig(CdpDeployPlan plan)
    {
        var serviceConfig = Path.Combine(plan.ServicePublishRoot, "cdp-mcp.toml");
        EnsureConfigAt(plan.ServicePublishRoot, serviceConfig);
        EnsureConfigAt(plan.BridgePublishRoot, serviceConfig);
    }

    static void EnsureConfigAt(string deployRoot, string fallbackConfig)
    {
        var dst = Path.Combine(deployRoot, "cdp-mcp.toml");
        if (File.Exists(dst))
            return;

        if (File.Exists(fallbackConfig))
            File.Copy(fallbackConfig, dst, true);
    }

    static void CopyTsWorker(string repoRoot, string deployRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "..", "guiders-core", "src", "TypescriptLang.Core", "worker"),
            Path.Combine(repoRoot, "..", "typescript-lang", "worker")
        };

        var workerSrc = candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.mjs")));
        if (workerSrc is null)
            return;

        var workerDst = Path.Combine(deployRoot, "ts-worker");
        if (Directory.Exists(workerDst))
            Directory.Delete(workerDst, true);

        CopyTree(workerSrc, workerDst);
    }

    static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}

internal static class CdpReloadNudge
{
    public static void TryBumpSeats(params string[] servers)
    {
        try
        {
            var mcpJson = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor", "mcp.json");
            if (!File.Exists(mcpJson))
                return;

            var raw = File.ReadAllText(mcpJson);
            if (!raw.Contains("CDP_RELOAD_NUDGE", StringComparison.Ordinal))
                return;

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("mcpServers", out _))
                return;

            foreach (var server in servers)
            {
                var pattern = $"\"{server}\"[\\s\\S]*?\"CDP_RELOAD_NUDGE\"\\s*:\\s*\"[^\"]*\"";
                if (!Regex.IsMatch(raw, pattern, RegexOptions.CultureInvariant))
                    continue;

                raw = Regex.Replace(
                    raw,
                    $"({Regex.Escape(server)}[\\s\\S]*?\"CDP_RELOAD_NUDGE\"\\s*:\\s*)\"[^\"]*\"",
                    $"$1\"{stamp}\"",
                    RegexOptions.CultureInvariant);
            }

            File.WriteAllText(mcpJson, raw);
        }
        catch
        {
            /* best effort */
        }
    }
}
