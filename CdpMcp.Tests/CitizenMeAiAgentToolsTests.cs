#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenMeAiAgentToolsTests
{
    [Fact]
    public void BuildWholeCatalog_covers_meta_and_dispatch()
    {
        var traces = new List<string>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult($"ok:{name}");

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, traces);
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(CitizenMeAiAgentTools.CountNamedCatalogTools() >= 80, "whole Meta+bare catalog expected");
        Assert.True(tools.Count >= CitizenMeAiAgentTools.CountNamedCatalogTools() + 1, "named + cdp_call");
        Assert.Contains("cdp_call", names);
        Assert.Contains("cdp_buffer", names);
        Assert.Contains("cdp_health", names);
        Assert.Contains("find", names);
    }
}
