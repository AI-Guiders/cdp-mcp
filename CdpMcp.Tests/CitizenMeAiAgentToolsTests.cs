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

        Assert.Equal(9, CitizenMeAiAgentTools.CountNamedCatalogTools());
        Assert.Equal(10, CitizenMeAiAgentTools.CountDispatchTools());
        Assert.Equal(10, tools.Count);
        Assert.Contains("cdp_call", names);
        Assert.Contains("cdp_health", names);
        Assert.Contains("cdp_buffer", names);
        Assert.Contains("find", names);
        Assert.Contains("cdp_open", names);
        Assert.Contains("cdp_test", names);
        Assert.Contains("open", names);
        Assert.Contains("edit", names);
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

    [Fact]
    public async Task Open_invent_alias_dispatches_cdp_open()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        string? seen = null;
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct)
        {
            seen = name;
            return Task.FromResult("opened");
        }

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied);
        var open = Assert.IsAssignableFrom<AIFunction>(
            tools.Single(t => string.Equals(t.Name, "open", StringComparison.OrdinalIgnoreCase)));

        var result = await open.InvokeAsync(
            new AIFunctionArguments { ["path"] = "GlassIntercomMention.cs" },
            cancellationToken: CancellationToken.None);

        Assert.Equal("cdp_open", seen);
        Assert.Contains("opened", result?.ToString() ?? "", StringComparison.Ordinal);
        Assert.True(applied[0].Ok);
        Assert.Equal("open", applied[0].Go);
    }

    [Fact]
    public async Task Edit_invent_alias_defaults_op_edit_on_cdp_buffer()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        string? seenTool = null;
        string? seenOp = null;
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct)
        {
            seenTool = name;
            if (args is not null && args.TryGetValue("op", out var opEl))
                seenOp = opEl.GetString();
            return Task.FromResult("edited");
        }

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied);
        var edit = Assert.IsAssignableFrom<AIFunction>(
            tools.Single(t => string.Equals(t.Name, "edit", StringComparison.OrdinalIgnoreCase)));

        var result = await edit.InvokeAsync(
            new AIFunctionArguments { ["path"] = "GlassIntercomMention.cs" },
            cancellationToken: CancellationToken.None);

        Assert.Equal("cdp_buffer", seenTool);
        Assert.Equal("edit", seenOp);
        Assert.Contains("edited", result?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Equal("edit", applied[0].Go);
    }

    [Fact]
    public async Task Concurrent_tool_invokes_lock_Applied_receipts()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        var gate = new object();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.Delay(20, ct).ContinueWith(_ => $"ok:{name}", ct);

        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied, gate);
        var health = Assert.IsAssignableFrom<AIFunction>(
            tools.Single(t => string.Equals(t.Name, "cdp_health", StringComparison.OrdinalIgnoreCase)));
        var find = Assert.IsAssignableFrom<AIFunction>(
            tools.Single(t => string.Equals(t.Name, "find", StringComparison.OrdinalIgnoreCase)));

        await Task.WhenAll(
            health.InvokeAsync(new AIFunctionArguments { ["args_json"] = "{}" }, cancellationToken: CancellationToken.None).AsTask(),
            find.InvokeAsync(new AIFunctionArguments { ["query"] = "ExpandWakes" }, cancellationToken: CancellationToken.None).AsTask());

        Assert.Equal(2, applied.Count);
        Assert.All(applied, a => Assert.True(a.Ok));
    }
}
