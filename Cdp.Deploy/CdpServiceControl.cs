using System.Diagnostics;
using System.Net.Http;

namespace Cdp.Deploy;

public static class CdpServiceControl
{
    const string DefaultHealthUrl = "http://127.0.0.1:8771/healthz";

    public static void StopLockHoldersUnder(string root, bool serviceOnly = false)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        var selfPid = Environment.ProcessId;

        // ADR-0212: kill → verify dead → retry. A single Kill+Sleep(800) let survivors
        // re-lock the payload mid-promote (robocopy exit=11 twice). Passes with liveness
        // checks close the window: the promote proceeds only over a verified-dead root.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var killedAny = false;

            foreach (var name in new[] { "CdpService", "CdpMcp", "CdpMcpBridge" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (path is null
                            || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Never kill the calling process tree — the orchestrator delegates
                        // its own restart to the supervisor (ADR-0203).
                        if (proc.Id == selfPid)
                            continue;

                        // Bridges keep their own install root; service-only deploys skip them.
                        if (serviceOnly && name == "CdpMcpBridge")
                            continue;

                        proc.Kill(entireProcessTree: true);
                        killedAny = true;
                    }
                    catch
                    {
                        /* best effort */
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
                    .Where(p => p.Id != selfPid)
                    .Any(p =>
                    {
                        try { return p.MainModule?.FileName?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true; }
                        catch { return false; }
                    });
                if (!alive)
                    break;
                Thread.Sleep(250);
            }
        }
    }



    public static void EnsureServiceExecutable(CdpDeployLayout layout)
    {
        var exe = Path.Combine(layout.ServiceInstall, "CdpService.exe");
        var fallback = Path.Combine(layout.ServiceInstall, "CdpMcp.exe");
        if (!File.Exists(exe) && !File.Exists(fallback))
        {
            throw new FileNotFoundException(
                "CdpService.exe missing after promote — staged tree was invalid.",
                exe);
        }
    }

    public static void StartService(CdpDeployLayout layout)
    {
        EnsureServiceExecutable(layout);
        var exe = Path.Combine(layout.ServiceInstall, "CdpService.exe");
        if (!File.Exists(exe))
            exe = Path.Combine(layout.ServiceInstall, "CdpMcp.exe");

        var config = CdpDeploySeatConfig.ResolveSeatConfigPath(layout.ServiceInstall)
                     ?? throw new FileNotFoundException(
                         $"Operator config missing at {CdpDeploySeatConfig.SeatConfigPath(layout.ServiceInstall)}.",
                         CdpDeploySeatConfig.SeatConfigPath(layout.ServiceInstall));
        if (TryHealth(DefaultHealthUrl))
            return;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--service --config \"{config}\"",
            WorkingDirectory = layout.ServiceInstall,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(psi);

        AssertHealthy(layout);
    }

    public static void AssertHealthy(CdpDeployLayout layout)
    {
        EnsureServiceExecutable(layout);
        if (!WaitHealthy(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("CdpService did not become healthy within 15s.");
    }

    public static bool WaitHealthy(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (TryHealth(DefaultHealthUrl))
                return true;
            Thread.Sleep(500);
        }

        return false;
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
}
