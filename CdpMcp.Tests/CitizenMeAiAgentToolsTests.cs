#nullable enable
using Microsoft.Extensions.AI;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenMeAiAgentToolsTests
{
    [Fact]
    public void BuildWholeCatalog_is_thin_habitat_plus_cdp_call()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult($"ok:{name}");

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied);
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(5, CitizenMeAiAgentTools.CountNamedCatalogTools());
        Assert.Equal(6, CitizenMeAiAgentTools.CountDispatchTools());
        Assert.Equal(6, tools.Count);
        Assert.Contains("cdp_call", names);
        Assert.Contains("cdp_health", names);
        Assert.Contains("cdp_buffer", names);
        Assert.Contains("find", names);
        Assert.DoesNotContain("cdp_pressure", names);
    }

    [Fact]
    public async Task Named_tool_records_Applied_for_SoftOrgan_HND()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult("{\"ok\":true}");

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied);
        var health = Assert.IsAssignableFrom<AIFunction>(
            tools.Single(t => string.Equals(t.Name, "cdp_health", StringComparison.OrdinalIgnoreCase)));

        var result = await health.InvokeAsync(
            new AIFunctionArguments { ["args_json"] = "{}" },
            cancellationToken: CancellationToken.None);

        Assert.Contains("ok", result?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Single(applied);
        Assert.True(applied[0].Ok);
        Assert.Equal("cdp_health", applied[0].Go);
        Assert.Contains("chars=", applied[0].Pulse, StringComparison.Ordinal);

        // SoftOrgan HND path = existing CideHandsLatch.PublishDone(FromApplied) — covered by CideHandsLatchTests.
        var hint = CitizenHandsReceipt.FormatChromeHint(
            CitizenHandsReceipt.FromApplied(applied, TimeSpan.FromSeconds(2)));
        Assert.Contains("OK", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cdp_health", hint, StringComparison.OrdinalIgnoreCase);
    }
}
