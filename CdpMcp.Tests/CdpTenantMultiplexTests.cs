#nullable enable
using Cdp.Core;
using Microsoft.AspNetCore.Http;
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
    public void Registry_creates_distinct_slices_per_key()
    {
        var settings = CdpSettings.Load(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "config", "cdp-mcp.toml"));
        var session = new SessionContext();
        var doc = new DocumentBufferStore();
        var ws = new WorkspaceDbHost(settings.IntentWorkspace.DatabasePath, session);
        var registry = new CdpTenantRegistry(
            settings,
            new CdpTenantSlice(CdpTenantKey.LegacyDefault, session, doc, ws, CdpProfile.StateRoot));

        var a = registry.Resolve(CdpTenantKey.Normalize("a", "ws1", "main"));
        var b = registry.Resolve(CdpTenantKey.Normalize("b", "ws1", "main"));
        Assert.NotSame(a, b);
        Assert.NotSame(a.Session, b.Session);
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
