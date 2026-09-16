using System.Diagnostics;
using Cdp.Config;

namespace CdpMcpBridge;

/// <summary>
/// ADR-0209 bridge bootstrap: when the tower (:8771) is down, spawn
/// <c>CdpGatekeeper.exe --gatekeeper</c> before slot auto-start.
/// Logon Scheduled Task remains cold-boot only; runtime recovery mirrors slot ensurer.
/// </summary>
internal sealed class CdpBridgeGatekeeperEnsurer
{
    static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    const string DefaultInstallDir = @"D:\cdp-gatekeeper";

    readonly CdpBridgeSettings _settings;
    readonly Uri _healthUrl;
    readonly HttpClient _probe;
    readonly string? _installDir;

    internal CdpBridgeGatekeeperEnsurer(CdpBridgeSettings settings, string? installDir)
    {
        _settings = settings;
        _installDir = installDir;
        _healthUrl = new Uri(settings.BaseUrl, "healthz");
        _probe = new HttpClient { Timeout = ProbeTimeout };
    }

    internal static CdpBridgeGatekeeperEnsurer? TryCreate(CdpBridgeSettings settings)
    {
        if (!settings.AutoStartTower)
            return null;

        var installDir = ResolveInstallDir(settings.GatekeeperInstallDir, settings.InstallDir);
        return installDir is null ? null : new CdpBridgeGatekeeperEnsurer(settings, installDir);
    }

    internal bool CanAutoStart => _installDir is not null;

    internal async Task<bool> TryEnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (!CanAutoStart)
            return false;

        if (await ProbeHealthyAsync(cancellationToken).ConfigureAwait(false))
            return true;

        _ = TryStartUnderProcessLock();
        return await WaitUntilHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    bool TryStartUnderProcessLock()
    {
        var lockPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            $"gatekeeper-start-{_settings.BaseUrl.Port}.lock");

        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        FileStream? lockStream = null;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            if (ProbeHealthySync())
                return false;

            if (!TryStartGatekeeperProcess(out var startError))
            {
                Console.Error.WriteLine($"CdpBridgeGatekeeperEnsurer: {startError}");
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    internal static string? ResolveInstallDir(string? configuredDir, string? slotInstallDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredDir))
            return Path.GetFullPath(configuredDir.Trim());

        if (Directory.Exists(DefaultInstallDir)
            && File.Exists(Path.Combine(DefaultInstallDir, "CdpGatekeeper.exe")))
            return DefaultInstallDir;

        if (!string.IsNullOrWhiteSpace(slotInstallDir)
            && File.Exists(Path.Combine(slotInstallDir, "CdpGatekeeper.exe")))
            return slotInstallDir;

        return null;
    }

    internal static string? ResolveGatekeeperExe(string installDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDir);
        var primary = Path.Combine(installDir, "CdpGatekeeper.exe");
        return File.Exists(primary) ? primary : null;
    }

    internal static string ResolveGatekeeperConfig(CdpBridgeSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ServiceConfigPath)
            ? CdpConfigPaths.DefaultBridgeConfigPath
            : settings.ServiceConfigPath;

    bool ProbeHealthySync()
    {
        try
        {
            using var response = _probe.GetAsync(_healthUrl).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (CdpBridgeServiceEnsurer.IsConnectionFailure(ex) || ex is TaskCanceledException)
        {
            return false;
        }
    }

    async Task<bool> ProbeHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _probe.GetAsync(_healthUrl, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (CdpBridgeServiceEnsurer.IsConnectionFailure(ex) || ex is TaskCanceledException)
        {
            return false;
        }
    }

    bool TryStartGatekeeperProcess(out string? error)
    {
        error = null;
        var installDir = _installDir!;
        var exe = ResolveGatekeeperExe(installDir);
        if (exe is null)
        {
            error = $"CdpGatekeeper.exe not found under gatekeeper install_dir '{installDir}'.";
            return false;
        }

        var configPath = ResolveGatekeeperConfig(_settings);
        Console.Error.WriteLine($"CdpBridgeGatekeeperEnsurer: starting {exe}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--gatekeeper --config \"{configPath}\"",
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
            error = $"Failed to start CdpGatekeeper: {ex.Message}";
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
