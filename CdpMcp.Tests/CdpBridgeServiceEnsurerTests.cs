#nullable enable
using System.Net;
using System.Net.Sockets;
using CdpMcpBridge;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpBridgeServiceEnsurerTests
{
    [Fact]
    public void ResolveServiceExe_prefers_CdpService_then_CdpMcp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-bridge-ensurer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fallback = Path.Combine(dir, "CdpMcp.exe");
            File.WriteAllText(fallback, "");

            Assert.Equal(fallback, CdpBridgeServiceEnsurer.ResolveServiceExe(dir));

            var primary = Path.Combine(dir, "CdpService.exe");
            File.WriteAllText(primary, "");
            Assert.Equal(primary, CdpBridgeServiceEnsurer.ResolveServiceExe(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveServiceExe_returns_null_when_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-bridge-ensurer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(CdpBridgeServiceEnsurer.ResolveServiceExe(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveServiceConfig_prefers_install_dir_toml()
    {
        var install = Path.Combine(Path.GetTempPath(), "cdp-bridge-seat-" + Guid.NewGuid().ToString("N"));
        var bridgeCfg = Path.Combine(Path.GetTempPath(), "cdp-bridge-cfg-" + Guid.NewGuid().ToString("N") + ".toml");
        Directory.CreateDirectory(install);
        var seatToml = Path.Combine(install, "cdp-mcp.toml");
        File.WriteAllText(seatToml, "[service]\n");
        File.WriteAllText(bridgeCfg, "[service]\n");
        try
        {
            var settings = new CdpBridgeSettings
            {
                BaseUrl = new Uri("http://127.0.0.1:8771/"),
                Token = "t",
                InstallDir = install,
                ServiceConfigPath = bridgeCfg,
                AutoStart = true
            };

            Assert.Equal(seatToml, CdpBridgeServiceEnsurer.ResolveServiceConfig(settings));
        }
        finally
        {
            Directory.Delete(install, recursive: true);
            File.Delete(bridgeCfg);
        }
    }

    [Fact]
    public void CanAutoStart_requires_install_dir_and_flag()
    {
        var baseSettings = new CdpBridgeSettings
        {
            BaseUrl = new Uri("http://127.0.0.1:8771/"),
            Token = "t",
            AutoStart = true,
            InstallDir = @"D:\cdp-service"
        };
        Assert.True(new CdpBridgeServiceEnsurer(baseSettings).CanAutoStart);

        var disabled = new CdpBridgeSettings
        {
            BaseUrl = baseSettings.BaseUrl,
            Token = baseSettings.Token,
            AutoStart = false,
            InstallDir = baseSettings.InstallDir
        };
        Assert.False(new CdpBridgeServiceEnsurer(disabled).CanAutoStart);

        var noDir = new CdpBridgeSettings
        {
            BaseUrl = baseSettings.BaseUrl,
            Token = baseSettings.Token,
            AutoStart = true,
            InstallDir = null
        };
        Assert.False(new CdpBridgeServiceEnsurer(noDir).CanAutoStart);
    }

    [Fact]
    public void IsConnectionFailure_detects_http_and_socket_errors()
    {
        Assert.True(CdpBridgeServiceEnsurer.IsConnectionFailure(new HttpRequestException("refused")));
        Assert.True(CdpBridgeServiceEnsurer.IsConnectionFailure(new SocketException()));
        Assert.False(CdpBridgeServiceEnsurer.IsConnectionFailure(new InvalidOperationException("logic")));
    }
}
