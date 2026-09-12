#nullable enable
using Cdp.Config;
using CdpMcp;
using CdpMcpBridge;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpConfigLoaderTests
{
    [Fact]
    public void MapBridge_legacy_service_only_builds_base_url_and_auto_start()
    {
        var doc = CdpConfigLoader.Parse("""
            [service]
            bind = "127.0.0.1"
            port = 8771
            install_dir = "D:/cdp-service"
            """);

        var bridge = CdpConfigLoader.MapBridge(doc);

        Assert.Equal(new Uri("http://127.0.0.1:8771/"), bridge.BaseUrl);
        Assert.Equal(Path.GetFullPath("D:/cdp-service"), bridge.InstallDir);
        Assert.True(bridge.AutoStart);
    }

    [Fact]
    public void MapBridge_prefers_bridge_section_over_legacy_service()
    {
        var doc = CdpConfigLoader.Parse("""
            [bridge]
            base_url = "http://127.0.0.1:9001"
            auto_start_slot = false

            [service]
            bind = "127.0.0.1"
            port = 8771
            install_dir = "D:/cdp-service"
            auto_start = true
            """);

        var bridge = CdpConfigLoader.MapBridge(doc);

        Assert.Equal(new Uri("http://127.0.0.1:9001/"), bridge.BaseUrl);
        Assert.False(bridge.AutoStart);
    }

    [Fact]
    public void MapBridge_reads_slots_install_dir_before_legacy_service()
    {
        var doc = CdpConfigLoader.Parse("""
            [slots]
            install_dir = "D:/seat"
            auto_start = false

            [service]
            install_dir = "D:/legacy"
            auto_start = true
            """);

        var bridge = CdpConfigLoader.MapBridge(doc);

        Assert.Equal(Path.GetFullPath("D:/seat"), bridge.InstallDir);
        Assert.False(bridge.AutoStart);
    }

    [Fact]
    public void MapSlot_reads_slots_section_before_legacy_service()
    {
        var doc = CdpConfigLoader.Parse("""
            [slots]
            enabled = false
            bind = "10.0.0.5"

            [service]
            enabled = true
            bind = "127.0.0.1"
            """);

        var slot = CdpConfigLoader.MapSlot(doc);

        Assert.False(slot.Enabled);
        Assert.Equal("10.0.0.5", slot.Bind);
    }

    [Fact]
    public void Bridge_loader_forbids_service_url_env_override()
    {
        Environment.SetEnvironmentVariable("CDP_SERVICE_URL", "http://127.0.0.1:9999");
        try
        {
            var load = CdpBridgeConfigLoader.Load(["--config", "D:/missing/cdp-mcp.toml"]);
            Assert.False(load.IsSuccess);
            Assert.Contains("CDP_SERVICE_URL is forbidden", load.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_SERVICE_URL", null);
        }
    }

    [Fact]
    public void ResolveServiceConfigPath_prefers_bridge_ssot_over_seat_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-config-paths-" + Guid.NewGuid().ToString("N"));
        var bridgeSeat = Path.Combine(root, "bridge");
        var serviceSeat = Path.Combine(root, "service");
        Directory.CreateDirectory(bridgeSeat);
        Directory.CreateDirectory(serviceSeat);

        var bridgeToml = Path.Combine(bridgeSeat, "cdp-mcp.toml");
        var serviceToml = Path.Combine(serviceSeat, "cdp-mcp.toml");
        File.WriteAllText(bridgeToml, "[bridge]\nbase_url = \"http://127.0.0.1:8771\"\n");
        File.WriteAllText(serviceToml, "[bridge]\nbase_url = \"http://127.0.0.1:9000\"\n");

        var prior = Environment.GetEnvironmentVariable("CDP_MCP_CONFIG");
        Environment.SetEnvironmentVariable("CDP_MCP_CONFIG", bridgeToml);
        try
        {
            var resolved = CdpConfigPaths.ResolveServiceConfigPath(null, serviceSeat);
            Assert.Equal(Path.GetFullPath(bridgeToml), Path.GetFullPath(resolved));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CDP_MCP_CONFIG", prior);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MapSlot_legacy_service_preserves_enabled_bind_port_token_path()
    {
        var doc = CdpConfigLoader.Parse("""
            [service]
            enabled = false
            bind = "10.0.0.1"
            port = 9000
            token_path = "D:/token"
            """);

        var slot = CdpConfigLoader.MapSlot(doc);

        Assert.False(slot.Enabled);
        Assert.Equal("10.0.0.1", slot.Bind);
        Assert.Equal(0, slot.Port);
        Assert.Equal("D:/token", slot.TokenPath);
    }

    [Fact]
    public void MapTower_prefers_tower_listen_port_over_legacy_service_port()
    {
        var doc = CdpConfigLoader.Parse("""
            [tower]
            listen_port = 9001

            [service]
            port = 8771
            """);

        var tower = CdpConfigLoader.MapTower(doc);

        Assert.Equal(9001, tower.ListenPort);
    }

    [Fact]
    public void MapForRole_defaults_when_sections_missing()
    {
        var doc = new CdpConfigDocument();

        Assert.Equal(CdpTowerRoleConfig.DefaultListenPort, CdpConfigLoader.MapForRole<CdpTowerRoleConfig>(doc, CdpConfigRole.Tower).ListenPort);
        Assert.Equal(0, CdpConfigLoader.MapForRole<CdpSlotRoleConfig>(doc, CdpConfigRole.Slot).Port);
        Assert.Equal(new Uri("http://127.0.0.1:8771/"), CdpConfigLoader.MapForRole<CdpBridgeRoleConfig>(doc, CdpConfigRole.Bridge).BaseUrl);
    }

    [Fact]
    public void CdpSettings_Load_reads_legacy_service_without_behavior_change()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-config-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "cdp-mcp.toml");
        File.WriteAllText(path, """
            [service]
            enabled = false
            bind = "10.0.0.2"
            port = 9002
            token_path = "D:/seat-token"
            """);

        try
        {
            var settings = CdpSettings.Load(path);

            Assert.False(settings.Service.Enabled);
            Assert.Equal("10.0.0.2", settings.Service.Bind);
            Assert.Equal(0, settings.Service.Port);
            Assert.Equal("D:/seat-token", settings.Service.TokenPath);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Bridge_loader_reads_bridge_base_url_from_new_section()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cdp-bridge-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var tokenPath = Path.Combine(dir, "service-token");
        File.WriteAllText(tokenPath, "bridge-token");
        var cfg = Path.Combine(dir, "cdp-mcp.toml");
        File.WriteAllText(cfg, $$"""
            [bridge]
            base_url = "http://127.0.0.1:9003"
            token_path = "{{tokenPath.Replace("\\", "/")}}"
            auto_start_slot = false
            """);

        try
        {
            var load = CdpBridgeConfigLoader.Load(["--config", cfg]);

            Assert.True(load.IsSuccess);
            Assert.NotNull(load.Settings);
            Assert.Equal(new Uri("http://127.0.0.1:9003/"), load.Settings!.BaseUrl);
            Assert.True(File.Exists(load.Settings.TokenPath!));
            Assert.False(load.Settings.AutoStart);
            Assert.Equal("bridge-token", load.Settings.Token);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_example_template_parses_bootstrap_sections()
    {
        var example = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "config",
            "cdp-mcp.toml.example");
        example = Path.GetFullPath(example);
        if (!File.Exists(example))
            example = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "config",
                "cdp-mcp.toml.example"));

        Assert.True(File.Exists(example), $"Missing template: {example}");

        var doc = CdpConfigLoader.Load(example);

        Assert.NotNull(doc.Tower);
        Assert.Equal(8771, doc.Tower!.ListenPort);
        Assert.NotNull(doc.Bridge);
        Assert.Equal("http://127.0.0.1:8771", doc.Bridge!.BaseUrl);
    }
}
