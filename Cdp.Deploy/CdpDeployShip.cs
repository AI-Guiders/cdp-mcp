using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Cdp.Deploy;

/// <summary>
/// ADR-0209 stage 3 (ship): a deploy is "start one more process", not "rewrite the live root".
/// The new slot starts from an immutable snapshot under <c>{ServiceInstall}.staging</c>; the
/// gatekeeper re-routes to the freshest healthy slot on its own. Old slots retire only after the
/// new one answers healthy. The live root is synced afterwards — over a verified-dead directory —
/// purely as the last-good cold-boot image. Rollback = start a previous snapshot (kept on disk).
/// </summary>
public static class CdpDeployShip
{
    /// <summary>Env var the slot host reads to bind a caller-pinned port (deterministic verify).</summary>
    public const string SlotPortEnvVar = "CDP_SLOT_PORT";

    // Slot port range — protocol constant, mirrors CdpSlotRegistry (ADR-0209).
    const int FirstSlotPort = 8772;
    const int LastSlotPort = 8871;

    /// <summary>How long a fresh slot may take to answer its own /healthz.</summary>
    public static readonly TimeSpan SlotHealthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Immutable snapshots retained for rollback (newest N survive pruning).</summary>
    public const int SnapshotsToKeep = 3;

    /// <summary>Allocate a fresh immutable snapshot dir: <c>&lt;stagingRoot&gt;/yyyyMMdd-HHmmss_&lt;guid32&gt;</c>.</summary>
    public static string AllocateSnapshotDir(string stagingRoot)
    {
        Directory.CreateDirectory(stagingRoot);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var dir = Path.Combine(stagingRoot, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{Guid.NewGuid():N}");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        throw new IOException($"Cannot allocate a unique snapshot dir under {stagingRoot}.");
    }

    /// <summary>
    /// Move a staged tree into its immutable snapshot. Same-volume move is atomic; a cross-volume
    /// fallback copies then deletes. The snapshot is never modified after this point.
    /// </summary>
    public static void MoveToSnapshot(string stagedDir, string snapshotDir)
    {
        try
        {
            Directory.Move(stagedDir, snapshotDir);
        }
        catch (IOException)
        {
            CopyTree(stagedDir, snapshotDir);
            Directory.Delete(stagedDir, recursive: true);
        }
    }

    /// <summary>First free TCP port in the slot range (mirrors CdpSlotRegistry.PickFreePort).</summary>
    public static int PickFreeSlotPort()
    {
        for (var port = FirstSlotPort; port <= LastSlotPort; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                /* occupied — next */
            }
            finally
            {
                try { listener.Stop(); }
                catch { /* best effort */ }
            }
        }

        throw new IOException($"No free slot port in {FirstSlotPort}..{LastSlotPort}.");
    }

    /// <summary>
    /// Poll the new slot's own /healthz (no tower, no registry — the endpoint is pinned).
    /// Fails fast if the process died; throws on timeout. On any failure the caller must kill
    /// the process — old slots are still running, so a failed ship costs nothing.
    /// </summary>
    public static void WaitSlotHealthy(int port, Process proc, TimeSpan? timeout = null)
    {
        var effective = timeout ?? SlotHealthTimeout;
        var deadline = DateTime.UtcNow + effective;
        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited)
                throw new InvalidOperationException($"Slot process exited early (code={proc.ExitCode}) before becoming healthy on :{port}.");

            if (TryHealth($"http://127.0.0.1:{port}/healthz"))
                return;

            Thread.Sleep(500);
        }

        throw new TimeoutException($"Slot on :{port} did not become healthy within {effective.TotalSeconds:0}s.");
    }

    /// <summary>Pure retire predicate (testable): a process is retired iff its exe lives under a retired root.</summary>
    public static bool ShouldRetire(string? exePath, int pid, int keepPid, int selfPid, IReadOnlyList<string> roots)
    {
        if (pid == keepPid || pid == selfPid || exePath is null)
            return false;

        foreach (var root in roots)
        {
            if (exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Retire every CdpService/CdpMcp process running from the live root or any previous snapshot —
    /// except the just-verified slot (and the caller). Kill → verify dead → retry, same discipline
    /// as StopLockHoldersUnder. In-flight requests on old slots break by design (ADR-0209: slots
    /// die only on our command, after the tower already routes to the fresh one).
    /// Returns the number of kill-issued processes; failures are logged to stderr, never swallowed.
    /// </summary>
    public static int RetireOtherSlots(IReadOnlyList<string> retiredRoots, int keepPid)
    {
        var selfPid = Environment.ProcessId;
        var roots = retiredRoots
            .Select(r => Path.GetFullPath(r).TrimEnd('\\', '/') + Path.DirectorySeparatorChar)
            .ToArray();

        var killed = 0;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var killedAny = false;
            foreach (var name in new[] { "CdpService", "CdpMcp" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (!ShouldRetire(path, proc.Id, keepPid, selfPid, roots))
                            continue;

                        try
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) // tree contains the caller
                        {
                            // The caller runs inside a CDP shell tab — a child of the very slot
                            // being retired. Tree-kill refuses; fall back to a plain kill (the
                            // slot's own children, e.g. ts-worker, orphan off — pruned later).
                            proc.Kill(entireProcessTree: false);
                        }

                        killedAny = true;
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"CdpDeployShip: retire kill failed pid={proc.Id} ({name}): {ex.Message}");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }

            if (!killedAny)
                break;

            // Wait for handles to actually release — Kill is async at the OS level.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                var alive = Process.GetProcessesByName("CdpService")
                    .Concat(Process.GetProcessesByName("CdpMcp"))
                    .Any(p =>
                    {
                        try { return ShouldRetire(p.MainModule?.FileName, p.Id, keepPid, selfPid, roots); }
                        catch { return false; }
                    });
                if (!alive)
                    break;
                Thread.Sleep(250);
            }
        }

        return killed;
    }

    /// <summary>Processes whose main executable lives under the given root (path-prefix safe).</summary>
    public static List<Process> ProcessesUnderDir(string root)
    {
        var result = new List<Process>();
        var prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var exe = proc.MainModule?.FileName;
                if (exe is not null && exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Sync the live root from the verified snapshot — only over a verified-dead directory.
    /// Returns false when something still runs from live (e.g. the caller itself): the ship stays
    /// valid (traffic is on the new slot), live simply catches up on the next ship.
    /// </summary>
    public static bool SyncLiveIfIdle(CdpDeployLayout layout, string snapshotDir)
    {
        var live = ProcessesUnderDir(layout.ServiceInstall);
        foreach (var p in live)
            p.Dispose();
        if (live.Count > 0)
            return false;

        CdpDeployPromoter.PromoteTree(snapshotDir, layout.ServiceInstall);
        return true;
    }

    /// <summary>Drop old snapshots beyond the retention window, skipping any still occupied.</summary>
    public static void PruneSnapshots(string stagingRoot, int keep = SnapshotsToKeep)
    {
        if (!Directory.Exists(stagingRoot))
            return;

        var dirs = Directory.GetDirectories(stagingRoot)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var dir in dirs.Skip(Math.Max(keep, 0)))
        {
            try
            {
                var live = ProcessesUnderDir(dir);
                foreach (var p in live)
                    p.Dispose();
                if (live.Count == 0)
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                /* best effort — a locked snapshot is retried on the next prune */
            }
        }
    }

    static bool TryHealth(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
