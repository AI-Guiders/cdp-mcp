using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeIgniteTenantPullForwardTests
{
    [Fact]
    public void ArmTenantWireEquals_matches_legacy_and_tenant_scopes()
    {
        var legacy = new IdeIgniteArmHost.IgniteArm();
        var tenant = new IdeIgniteArmHost.IgniteArm { TenantWire = "ws:abc:main" };

        Assert.True(IdeIgniteArmHost.ArmTenantWireEquals(legacy, null));
        Assert.False(IdeIgniteArmHost.ArmTenantWireEquals(tenant, null));
        Assert.True(IdeIgniteArmHost.ArmTenantWireEquals(tenant, "ws:abc:main"));
        Assert.False(IdeIgniteArmHost.ArmTenantWireEquals(tenant, "ws:other:main"));
    }

    [Fact]
    public void DistinctTenantWires_returns_legacy_scope_when_no_matching_arms()
    {
        var wires = IdeIgniteArmHost.DistinctTenantWiresFromArmedWorkTimers();
        Assert.Contains(null, wires);
    }
}
