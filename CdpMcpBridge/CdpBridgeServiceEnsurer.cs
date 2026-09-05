using System.Diagnostics;
using System.Net.Sockets;
using TerminalMcp.Core;

namespace CdpMcpBridge;

/// <summary>
/// ADR-0198 bridge bootstrap: when sidecar is down (deploy kill, cold boot after token exists),
/// start <c>CdpService.exe --service</c> from configured <c>install_dir</c> before HTTP retry.
/// </summary>
internal sealed class CdpBridgeServiceEnsurer
{
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    readonly CdpBridgeSettings _settings;
    readonly Uri _healthUrl;
    readonly HttpClient _probe;

    internal CdpBridgeServiceEnsurer(CdpBridgeSettings settings)
    {
        _settings = settings;
        _healthUrl = new Uri(settings.BaseUrl, "healthz");
        _probe = new HttpClient { Timeout = ProbeTimeout };
    }

    internal bool CanAutoStart =>
        _settings.AutoStart && !string.IsNullOrWhiteSpace(_settings.InstallDir);

    /// <summary>Supervisor owns CdpService restart during durable deploy — bridge must not race it.
    /// ADR-0211: the in-flight job check alone leaks when a stale lease expires mid-promote;
    /// the time-fenced deploy.lock in the install dir is the honest fence.</summary>
    internal bool ShouldSuppressAutoStart() =>
        DurableJobStore.TryGetInFlightKind("deploy") is not null || IsDeployLockFresh();

    internal bool IsDeployLockFresh()
    {
        if (string.IsNullOrWhiteSpace(_settings.InstallDir))
            return false;
        var lockPath = Path.Combine(_settings.InstallDir, "deploy.lock");
        if (!File.Exists(lockPath))
            return false;
        return DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(lockPath) < TimeSpan.FromMinutes(5);
    }

    internal async Task<bool> TryEnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false))
            return true;

        if (!CanAutoStart || ShouldSuppressAutoStart())
            return false;

        _ = TryStartUnderProcessLock();
        return await WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cross-process start gate — no await while lock held (Mutex is thread-affine; await resumes on another pool thread).
    /// </summary>
    bool TryStartUnderProcessLock()
    {
        var lockPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            $"service-start-{_settings.BaseUrl.Port}.lock");

        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            if (ProbeHealthySync())
                return false;

            if (ShouldSuppressAutoStart())
                return false;

            if (!TryStartServiceProcess(out var startError))
            {
                Console.Error.WriteLine($"CdpBridgeServiceEnsurer: {startError}");
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            // Another bridge instance is starting — caller will poll health outside the lock.
            return false;
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    bool ProbeHealthySync()
    {
        try
        {
            using var response = _probe.GetAsync(_healthUrl).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsConnectionFailure(ex) || ex is TaskCanceledException)
        {
            return false;
        }
    }

    internal static bool IsConnectionFailure(Exception ex) =>
        ex is HttpRequestException or SocketException
        || (ex.InnerException is not null && IsConnectionFailure(ex.InnerException));

    internal static string? ResolveServiceExe(string installDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDir);
        var primary = Path.Combine(installDir, "CdpService.exe");
        if (File.Exists(primary))
            return primary;
        var fallback = Path.Combine(installDir, "CdpMcp.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    internal static string ResolveServiceConfig(CdpBridgeSettings settings)
    {
        var installDir = settings.InstallDir;
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            var inSeat = Path.Combine(installDir, "cdp-mcp.toml");
            if (File.Exists(inSeat))
                return inSeat;

            var nested = Path.Combine(installDir, "config", "cdp-mcp.toml");
            if (File.Exists(nested))
                return nested;
        }

        if (!string.IsNullOrWhiteSpace(settings.ServiceConfigPath)
            && File.Exists(settings.ServiceConfigPath))
            return settings.ServiceConfigPath;

        throw new InvalidOperationException(
            "Cannot resolve service config for auto-start — set install_dir with cdp-mcp.toml or pass bridge --config.");
    }

    async Task<bool> ProbeHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _probe.GetAsync(_healthUrl, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (IsConnectionFailure(ex) || ex is TaskCanceledException)
        {
            return false;
        }
    }

    bool TryStartServiceProcess(out string? error)
    {
        error = null;
        var installDir = _settings.InstallDir!;
        var exe = ResolveServiceExe(installDir);
        if (exe is null)
        {
            error = $"CdpService.exe not found under install_dir '{installDir}'.";
            return false;
        }
        // ADR-0212: re-check the deploy fence at the last moment — between the outer
        // suppression check and this Process.Start the promote may have Acquire'd the
        // lock; starting now would re-lock the payload mid-promote.
        if (IsDeployLockFresh())
        {
            error = "deploy.lock fresh — promote in flight, bridge must not race it.";
            return false;
        }

        string configPath;
        try
        {
            configPath = ResolveServiceConfig(_settings);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        Console.Error.WriteLine($"CdpBridgeServiceEnsurer: starting {exe}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--service --config \"{configPath}\"",
            WorkingDirectory = installDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to start CdpService: {ex.Message}";
            return false;
        }
    }

    async Task<bool> WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false))
                return true;
            await Task.Delay(ReadyPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}
