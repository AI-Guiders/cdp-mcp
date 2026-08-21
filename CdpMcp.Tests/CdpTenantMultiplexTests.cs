#nullable enable
using Cdp.Core;
using CdpMcp.Backends;
using Microsoft.AspNetCore.Http;
using TerminalMcp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpTenantMultiplexTests
{
    [Fact]
    public void TenantKey_wire_and_sanitize()
    {
        var key = CdpTenantKey.Normalize("bridge-1", "wsAbc", "composer_main");
        Assert.Equal("bridge-1:wsAbc:composer_main", key.Wire);
        Assert.False(key.IsLegacyDefault);
    }

    [Fact]
    public void TenantHeaders_parse_from_http()
    {
        var headers = new HeaderDictionary
        {
            [CdpTenantHeaders.BridgeSession] = "b1",
            [CdpTenantHeaders.WorkspaceKey] = "cdp",
            [CdpTenantHeaders.Composer] = "main"
        };

        var key = CdpTenantHeaders.TryParse(headers);
        Assert.NotNull(key);
        Assert.Equal("b1:cdp:main", key.Value.Wire);
    }

    [Fact]
    public void ComposerLatch_tracks_bridge_session()
    {
        Assert.True(CdpTenantComposerLatch.TrySet("bridge-a", "chat-b"));
        Assert.Equal("chat-b", CdpTenantComposerLatch.Get("bridge-a"));
    }

    [Fact]
    public void Registry_creates_distinct_slices_with_isolated_shell()
    {
        var settings = CdpSettings.Load(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"));
        var session = new SessionContext();
        var doc = new DocumentBufferStore();
        var ws = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
        var shell = new ShellHabitat();
        var ideSettings = new IdeSettingsHabitat(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"),
            settings,
            session,
            shell,
            () => ProgramHost.ShellDefaults(session));
        var kernel = new CdpSharedKernel
        {
            ConfigPath = "config/cdp-mcp.toml",
            Settings = settings,
            Modules = [],
            ByDomain = new Dictionary<string, ICdpBackendModule>(),
            AllAffordances = [],
            McpVersion = "0.0.0",
            Pretty = new System.Text.Json.JsonSerializerOptions(),
            McpOutlet = new McpOutletHabitat(),
            InternetBrowser = new InternetBrowserHabitat(),
            AnTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            TkTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            FindTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            FailTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            DbgTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            BtTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            RoslynTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            GitTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            HciTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>(),
            AnuiTools = new Dictionary<string, ModelContextProtocol.Protocol.Tool>()
        };
        var registry = new CdpTenantRegistry(
            kernel,
            CdpTenantSliceFactory.WrapLegacy(
                CdpTenantKey.LegacyDefault,
                session,
                doc,
                ws,
                shell,
                ideSettings,
                null,
                CdpProfile.StateRoot));

        var a = registry.Resolve(CdpTenantKey.Normalize("a", "ws1", "main"));
        var b = registry.Resolve(CdpTenantKey.Normalize("b", "ws1", "main"));
        Assert.NotSame(a, b);
        Assert.NotSame(a.Session, b.Session);
        Assert.NotSame(a.Shell, b.Shell);
        Assert.Equal(2, registry.ActiveCount);
    }

    [Fact]
    public void Profile_tenant_scope_overrides_state_root()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CDP_PROFILE") ?? "default",
                "default",
                StringComparison.OrdinalIgnoreCase))
            return;

        var baseRoot = CdpProfile.StateRoot;
        var tenantRoot = Path.Combine(baseRoot, "tenant-test");
        using (CdpProfile.EnterTenantStateRoot(tenantRoot))
        {
            Assert.Equal(Path.GetFullPath(tenantRoot), Path.GetFullPath(CdpProfile.StateRoot));
        }

        Assert.Equal(baseRoot, CdpProfile.StateRoot);
    }
}
