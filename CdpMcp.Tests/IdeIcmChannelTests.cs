#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIcmChannelTests
{
    [Fact]
    public async Task Scene_reports_alias_count_and_host_profile_from_channel()
    {
        using var doc = JsonDocument.Parse(await IdeIcmChannel.HandleJsonAsync(null, CancellationToken.None));
        var root = doc.RootElement;
        Assert.Equal("icm_channel/v1", root.GetProperty("schema").GetString());
        Assert.True(root.GetProperty("alias_count").GetInt32() > 0);
        var pulse = CockpitHostProfile.Current();
        Assert.Equal(pulse.HostProfile, root.GetProperty("host_profile").GetString());
        Assert.Equal(pulse.GuiHost, root.GetProperty("gui_host").GetString());
    }

    [Fact]
    public async Task Aliases_lists_bucket_A()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("aliases")
        };
        using var doc = JsonDocument.Parse(await IdeIcmChannel.HandleJsonAsync(args, CancellationToken.None));
        Assert.True(doc.RootElement.GetProperty("count").GetInt32() >= 10);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("entries").ValueKind);
    }

    [Fact]
    public async Task Resolve_maps_build_to_cdp_build()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("resolve"),
            ["command_id"] = JsonSerializer.SerializeToElement("build")
        };
        using var doc = JsonDocument.Parse(await IdeIcmChannel.HandleJsonAsync(args, CancellationToken.None));
        Assert.True(doc.RootElement.GetProperty("mapped").GetBoolean());
        Assert.Equal("cdp_build", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task Invoke_forwards_via_ExecuteAliasedAsync()
    {
        string? seen = null;
        IdeCommandModule.Bind((id, a, _) =>
        {
            seen = id;
            return Task.FromResult("{\"ok\":true}");
        });
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("invoke"),
                ["command_id"] = JsonSerializer.SerializeToElement("run_tests")
            };
            using var doc = JsonDocument.Parse(await IdeIcmChannel.HandleJsonAsync(args, CancellationToken.None));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("cdp_test", seen);
            Assert.Equal("cdp_test", doc.RootElement.GetProperty("tool").GetString());
        }
        finally
        {
            IdeCommandModule.Unbind();
        }
    }
}
