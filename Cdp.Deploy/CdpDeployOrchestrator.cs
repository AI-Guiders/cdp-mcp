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
            CdpDeployMode.Ship => Ship(plan),
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
        // Prefix is path-segment safe: ServiceInstall must not match ".staging" siblings.
        var livePrefix = Path.GetFullPath(plan.Layout.ServiceInstall).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (AppContext.BaseDirectory.StartsWith(livePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Deploy worker runs from ServiceInstall — promote would self-lock (ADR-0211). " +
                "Deploy jobs run from their own clone or another seat.");

        var pending = CdpDeployPending.ReadRequired(plan.Layout);
        // Service part: staged root from pending. Empty/missing root = service already
        // landed (bridge-only apply after a deferred bridge wave).
        var serviceNext = ResolveStagedServiceRoot(pending, plan.Layout);

        // Bridges are client-owned (stdio children of the agent harness) — apply never
        // kills them. Bits staged while bridges run defer to the next apply: pending
        // keeps the bridge roots, and the bits land after a natural client restart.
        var bridgeDeferred = (
                BridgeStaged(pending.BridgeRoot)
                || BridgeStaged(plan.Layout.StagedBridgeRelease)
                || BridgeStaged(plan.Layout.StagedBridgeDebug))
            && (BridgeInstallBusy(plan.Layout.BridgeReleaseInstall)
                || BridgeInstallBusy(plan.Layout.BridgeDebugInstall));

        var lockJob = $"apply-{Guid.NewGuid():N}"[..12];
        CdpDeployLock.Acquire(plan.Layout.ServiceInstall, lockJob);
        var shippedService = false;
        try
        {
            if (serviceNext is not null)
            {
                // ADR-0209 stage 3: no promote over the live root. The staged tree moves into an
                // immutable snapshot; the new slot starts from it; old slots retire only after
                // the new one answers healthy. Live syncs afterwards — over a verified-dead dir.
                var snapshot = CdpDeployShip.AllocateSnapshotDir(plan.Layout.ServiceStagingRoot);
                CdpDeployShip.MoveToSnapshot(serviceNext, snapshot);
                try
                {
                    ActivateSnapshot(plan, snapshot);
                }
                catch
                {
                    // Keep the pending update retryable: re-point it at the moved snapshot.
                    CdpDeployPending.WriteSoft(plan.Layout, snapshot, pending.BridgeRoot, pending.Version);
                    throw;
                }

                shippedService = true;
            }

            if (!bridgeDeferred)
            {
                PromoteBridgeIfStaged(pending.BridgeRoot, plan.Layout.BridgeReleaseInstall);
                PromoteBridgeIfStaged(plan.Layout.StagedBridgeRelease, plan.Layout.BridgeReleaseInstall);
                PromoteBridgeIfStaged(plan.Layout.StagedBridgeDebug, plan.Layout.BridgeDebugInstall);
            }

            if (bridgeDeferred)
                CdpDeployPending.WriteDeferredBridge(plan.Layout, DeferredBridgeRoot(pending, plan));
            else
                CdpDeployPending.Clear(plan.Layout);

            CleanupStaged(plan.Layout.StagedService);
            if (!bridgeDeferred)
            {
                CleanupStaged(plan.Layout.StagedBridgeRelease);
                CleanupStaged(plan.Layout.StagedBridgeDebug);
            }

            if (!shippedService)
            {
                // Bridge-only apply (or nothing staged): legacy start path. A shipped service
                // is already started and health-verified by ActivateSnapshot.
                CdpServiceControl.StartService(plan.Layout);
                CdpServiceControl.AssertHealthy(plan.Layout);
            }

            if (!plan.NoNudge)
                CdpReloadNudge.TryBumpSeats("cdp", "cdp-debug");
        }
        finally
        {
            CdpDeployLock.Release(plan.Layout.ServiceInstall);
        }

        return new CdpDeployStepResult(
            true,
            bridgeDeferred
                ? $"apply ok service={plan.Layout.ServiceInstall} bridge=deferred (bridges running — lands on next apply)"
                : $"apply ok service={plan.Layout.ServiceInstall}",
            bridgeDeferred
                ? $"APPLY ok (bridge deferred) {plan.Layout.ServiceInstall}"
                : $"APPLY ok {plan.Layout.ServiceInstall}",
            0,
            null);
    }

    /// <summary>
    /// ADR-0209 stage 3: deploy = "start one more process". Publish staged exactly like soft,
    /// move the stage into an immutable snapshot, then activate. No promote over a live root —
    /// the robocopy-against-running-slot failure class (exit=11) dies by construction.
    /// </summary>
    static CdpDeployStepResult Ship(CdpDeployPlan plan)
    {
        var livePrefix = Path.GetFullPath(plan.Layout.ServiceInstall).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        if (AppContext.BaseDirectory.StartsWith(livePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Deploy worker runs from ServiceInstall — ship would self-lock on live sync (ADR-0211). " +
                "Deploy jobs run from their own clone or another seat.");

        // Publish can take minutes and touches nothing live — the deploy lock is taken only for
        // the short critical section (start → verify → retire → sync), well inside its TTL.
        if (Directory.Exists(plan.Layout.StagedService))
            Directory.Delete(plan.Layout.StagedService, recursive: true);
        PublishService(plan, killRunning: false);
        FinalizeSeatConfigs(plan);

        var snapshot = CdpDeployShip.AllocateSnapshotDir(plan.Layout.ServiceStagingRoot);
        CdpDeployShip.MoveToSnapshot(plan.Layout.StagedService, snapshot);

        int pid, port, retired;
        bool synced;
        var lockJob = $"ship-{Guid.NewGuid():N}"[..12];
        CdpDeployLock.Acquire(plan.Layout.ServiceInstall, lockJob);
        try
        {
            (pid, port, retired, synced) = ActivateSnapshot(plan, snapshot);
            CdpDeployPending.Clear(plan.Layout);
        }
        finally
        {
            CdpDeployLock.Release(plan.Layout.ServiceInstall);
        }

        CdpDeployShip.PruneSnapshots(plan.Layout.ServiceStagingRoot);
        if (!plan.NoNudge)
            CdpReloadNudge.TryBumpSeats("cdp", "cdp-debug");

        return new CdpDeployStepResult(
            true,
            $"ship ok slot_pid={pid} port={port} retired={retired} live_synced={synced} snapshot={snapshot}",
            $"SHIP ok :{port} {Path.GetFileName(snapshot)}",
            0,
            null);
    }

    /// <summary>
    /// Shared ship tail: start the slot from the snapshot, verify its pinned health endpoint,
    /// retire old slots, then sync the live root over the now-dead directory. Callers hold the
    /// deploy lock. A failed verify kills the fresh process and leaves old slots untouched.
    /// </summary>
    static (int Pid, int Port, int Retired, bool LiveSynced) ActivateSnapshot(CdpDeployPlan plan, string snapshotDir)
    {
        CdpServiceControl.EnsureServiceExecutableIn(snapshotDir);
        CdpDeploySeatConfig.NormalizeInstallSeat(snapshotDir);

        var port = CdpDeployShip.PickFreeSlotPort();
        using var proc = CdpServiceControl.StartSlotFromDir(snapshotDir, port);
        try
        {
            CdpDeployShip.WaitSlotHealthy(port, proc);
        }
        catch
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        var retired = CdpDeployShip.RetireOtherSlots(
            [plan.Layout.ServiceInstall, plan.Layout.ServiceStagingRoot],
            proc.Id);
        var synced = CdpDeployShip.SyncLiveIfIdle(plan.Layout, snapshotDir);
        return (proc.Id, port, retired, synced);
    }

    internal static string? ResolveStagedServiceRoot(CdpDeployPending.PendingUpdate pending, CdpDeployLayout layout)
    {
        if (!string.IsNullOrWhiteSpace(pending.ServiceRoot) && Directory.Exists(pending.ServiceRoot))
            return pending.ServiceRoot;
        return Directory.Exists(layout.StagedService) ? layout.StagedService : null;
    }

    static bool BridgeInstallBusy(string installRoot)
    {
        var live = LiveProcessesUnder(installRoot);
        foreach (var proc in live)
            proc.Dispose();
        return live.Count > 0;
    }

    static string DeferredBridgeRoot(CdpDeployPending.PendingUpdate pending, CdpDeployPlan plan)
    {
        if (BridgeStaged(pending.BridgeRoot))
            return pending.BridgeRoot;
        if (BridgeStaged(plan.Layout.StagedBridgeRelease))
            return plan.Layout.StagedBridgeRelease;
        return plan.Layout.StagedBridgeDebug ?? "";
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
    public static void TryBumpSeats(params string[] servers) =>
        CdpBridgeRevNudge.TryBumpSeats(servers);
}
