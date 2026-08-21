using System.Diagnostics;
using System.Net.Sockets;

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

    internal async Task<bool> TryEnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false))
            return true;

        if (!CanAutoStart)
            return false;

        var mutexName = $"Global\\CdpBridgeServiceEnsurer-{ _settings.BaseUrl.Port}";
        using var mutex = new Mutex(initiallyOwned: false, name: mutexName);
        var owns = false;
        try
        {
            try
            {
                owns = mutex.WaitOne(TimeSpan.FromSeconds(20));
            }
            catch (AbandonedMutexException)
            {
                owns = true;
            }

            if (!owns)
                return await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false);

            if (await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false))
                return true;

            if (!TryStartServiceProcess(out var startError))
            {
                Console.Error.WriteLine($"CdpBridgeServiceEnsurer: {startError}");
                return false;
            }

            return await WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (owns)
                mutex.ReleaseMutex();
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

internal static class CdpBridgeTransport
{
    internal static async Task<T> WithEnsureRetryAsync<T>(
        CdpBridgeServiceEnsurer ensurer,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt == 0 && CdpBridgeServiceEnsurer.IsConnectionFailure(ex))
            {
                if (!await ensurer.TryEnsureRunningAsync(cancellationToken).ConfigureAwait(false))
                    throw;
            }
        }

        throw new InvalidOperationException("CdpBridgeTransport retry exhausted.");
    }
}
