using System.Diagnostics;
using System.Net.Http;

namespace Cdp.Deploy;

public static class CdpServiceControl
{
    const string DefaultHealthUrl = "http://127.0.0.1:8771/healthz";

    public static void StopLockHoldersUnder(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        foreach (var name in new[] { "CdpService", "CdpMcp", "CdpMcpBridge" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (path is not null
                        && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        proc.Kill(entireProcessTree: true);
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

        Thread.Sleep(800);
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

        var config = Path.Combine(layout.ServiceInstall, "cdp-mcp.toml");
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
