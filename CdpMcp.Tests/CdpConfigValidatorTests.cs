#nullable enable
using Cdp.Config;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpConfigValidatorTests
{
    [Fact]
    public void Validate_warns_on_legacy_service_section()
    {
        var result = CdpConfigValidator.Validate("""
            [service]
            bind = "127.0.0.1"
            """, strict: false);

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Contains("[service]"));
    }

    [Fact]
    public void Validate_strict_fails_on_legacy_service_section()
    {
        var result = CdpConfigValidator.Validate("""
            [service]
            bind = "127.0.0.1"
            """, strict: true);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("[service]"));
    }

    [Fact]
    public void Validate_strict_fails_on_unknown_bootstrap_key()
    {
        var result = CdpConfigValidator.Validate("""
            [tower]
            mystery = 1
            """, strict: true);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("tower.mystery"));
    }

    [Fact]
    public void Validate_warns_on_unknown_bootstrap_key_by_default()
    {
        var result = CdpConfigValidator.Validate("""
            [bridge]
            mystery = 1
            """);

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Contains("bridge.mystery"));
    }
}
