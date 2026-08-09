#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenMeAiAgentToolsTests
{
    [Fact]
    public void BuildWholeCatalog_is_cdp_call_dispatch_not_schema_thrash()
    {
        var traces = new List<string>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult($"ok:{name}");

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, traces);
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(1, CitizenMeAiAgentTools.CountDispatchTools());
        Assert.Equal(0, CitizenMeAiAgentTools.CountNamedCatalogTools());
        Assert.Single(tools);
        Assert.Contains("cdp_call", names);
        Assert.DoesNotContain("cdp_buffer", names);
        Assert.DoesNotContain("find", names);
    }
}
