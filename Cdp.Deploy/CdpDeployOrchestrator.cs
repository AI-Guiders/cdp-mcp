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
        if (plan.BridgePublishRoot is { } bridgeRoot)
        {
            PublishBridgeSeat(plan, bridgeRoot);
            var debugRoot = plan.BridgeDebugPublishRoot;
            if (debugRoot is not null && !CdpDeployPaths.SamePath(bridgeRoot, debugRoot))
                PublishBridgeSeat(plan, debugRoot);
        }

        FinalizeSeatConfigs(plan);

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

        CdpDeployPending.WriteSoft(plan.Layout, plan.ServicePublishRoot, plan.BridgePublishRoot ?? "", version);
        return new CdpDeployStepResult(
            true,
            $"soft staged service={plan.ServicePublishRoot} bridge={plan.BridgePublishRoot ?? "service-only (ADR-0209)"}",
            $"SOFT staged {plan.ServicePublishRoot}",
            0,
            null);
    }

        static CdpDeployStepResult Hard(CdpDeployPlan plan)
    {
        // ADR-0211 spirit: a live seat cannot be republished under itself.
        // Hard is for seats with nothing running; live seats go soft + apply (tower applies pending).
        var live = LiveProcessesUnder(plan.Layout.ServiceInstall);
        if (live.Count > 0)
        {
            var names = new System.Text.StringBuilder();

            foreach (var p in live)
                names.Append(p.ProcessName).Append('(').Append(p.Id).Append(") ");

            foreach (var p in live)
                p.Dispose();

            throw new InvalidOperationException(
                $"Hard deploy refused: target install '{plan.Layout.ServiceInstall}' hosts live processes: {names}. " +
                "A live seat cannot be republished under itself (self-lock, ADR-0211). " +
                "Use mode=soft (stage) + mode=apply (tower applies pending), or mode=rollout (hot-standby rotation).");
        }

        PublishService(plan, killRunning: true);
        if (plan.BridgePublishRoot is { } bridgeRoot)
        {
            PublishBridgeSeat(plan, bridgeRoot);
            var debugRoot = plan.BridgeDebugPublishRoot;
            if (debugRoot is not null && !CdpDeployPaths.SamePath(bridgeRoot, debugRoot))
                PublishBridgeSeat(plan, debugRoot);
        }

        FinalizeSeatConfigs(plan);

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
        // ADR-0211: a promote must never be executed by a process whose own bits
        // live inside the target — the worker would hold its own exe (self-lock).
        // Deploy jobs run from a disposable clone (IDE lifecycle enqueue).
        if (AppContext.BaseDirectory.StartsWith(plan.Layout.ServiceInstall, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Deploy worker runs from ServiceInstall — promote would self-lock (ADR-0211). " +
                "Deploy jobs run from their own clone or another seat.");

        var pending = CdpDeployPending.ReadRequired(plan.Layout);
        var serviceNext = string.IsNullOrWhiteSpace(pending.ServiceRoot)
            ? plan.Layout.StagedService
            : pending.ServiceRoot;

        var lockJob = $"apply-{Guid.NewGuid():N}"[..12];
        CdpDeployLock.Acquire(plan.Layout.ServiceInstall, lockJob);
        try
        {
            CdpServiceControl.StopLockHoldersUnder(plan.Layout.ServiceInstall, serviceOnly: true);

            // Bridges are stopped only when their bits are actually staged —
            // stdio transports survive service-only deploys (hot-standby, ADR-0203+).
            if (BridgeStaged(pending.BridgeRoot) || BridgeStaged(plan.Layout.StagedBridgeRelease))
                CdpServiceControl.StopLockHoldersUnder(plan.Layout.BridgeReleaseInstall);
            if (BridgeStaged(plan.Layout.StagedBridgeDebug))
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
        }
        finally
        {
            CdpDeployLock.Release(plan.Layout.ServiceInstall);
        }

        return new CdpDeployStepResult(
            true,
            $"apply ok service={plan.Layout.ServiceInstall}",
            $"APPLY ok {plan.Layout.ServiceInstall}",
            0,
            null);
    }

    static bool BridgeStaged(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

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

    /// <summary>Processes whose main executable lives under the install root (ADR-0211 self-lock probe).</summary>
    static System.Collections.Generic.List<Process> LiveProcessesUnder(string installRoot)
    {
        var result = new System.Collections.Generic.List<Process>();
        var root = Path.GetFullPath(installRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var exe = proc.MainModule?.FileName;
                if (exe is not null && exe.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    result.Add(proc);
                else
                    proc.Dispose();
            }
            catch
            {
                proc.Dispose();
            }
        }

        return result;
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

        // ADR-0209: the eternal tower ships beside the service — separate exe name,
        // invisible to seat-process reclaim (the tower never kills, never gets killed).
        var gateExe = Path.Combine(plan.ServicePublishRoot, "CdpGatekeeper.exe");
        if (File.Exists(dstExe))
            File.Copy(dstExe, gateExe, overwrite: true);

        CopyTsWorker(plan.Source.RepoRoot, plan.ServicePublishRoot);
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
    }

        static void FinalizeSeatConfigs(CdpDeployPlan plan)
    {
        CdpDeploySeatConfig.SeedFromLiveSeat(plan.Layout.ServiceInstall, plan.ServicePublishRoot);
        CdpDeploySeatConfig.StripDevTemplate(plan.ServicePublishRoot);

        if (plan.BridgePublishRoot is not { } bridgeRoot)
            return;

        var liveBridge = CdpDeployPaths.ResolveLiveFromStaged(bridgeRoot, bridgeRoot);
        CdpDeploySeatConfig.SeedFromLiveSeat(liveBridge, bridgeRoot);
        CdpDeploySeatConfig.StripDevTemplate(bridgeRoot);

        if (plan.BridgeDebugPublishRoot is not null
            && !CdpDeployPaths.SamePath(bridgeRoot, plan.BridgeDebugPublishRoot))
        {
            var liveDebug = CdpDeployPaths.ResolveLiveFromStaged(
                plan.BridgeDebugPublishRoot,
                plan.Layout.BridgeDebugInstall);
            CdpDeploySeatConfig.SeedFromLiveSeat(liveDebug, plan.BridgeDebugPublishRoot);
            CdpDeploySeatConfig.StripDevTemplate(plan.BridgeDebugPublishRoot);
        }
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
