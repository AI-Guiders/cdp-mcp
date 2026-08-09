#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenMeAiAgentToolsTests
{
    [Fact]
    public void BuildWholeCatalog_is_thin_habitat_plus_cdp_call()
    {
        var traces = new List<string>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult($"ok:{name}");

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, traces);
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
}
