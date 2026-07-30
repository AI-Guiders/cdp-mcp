#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class EditorPlaneScenePulseTests
{
    [Fact]
    public async Task Editor_scene_default_is_pulse_snap_without_loci()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext();
        var json = await EditorPlane.DispatchAsync(
            "cdp_editor_scene",
            store,
            session,
            new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("editor_scene/v0", root.GetProperty("schema").GetString());
        Assert.Equal("pulse", root.GetProperty("detail").GetString());
        Assert.True(root.GetProperty("snap").GetBoolean());
        Assert.Equal("—", root.GetProperty("pulse").GetString());
        Assert.False(root.TryGetProperty("loci", out _));
        Assert.False(root.TryGetProperty("buffers", out _));
    }

    [Fact]
    public async Task Editor_scene_detail_full_includes_loci()
    {
        var store = new DocumentBufferStore();
        var session = new SessionContext();
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["detail"] = JsonSerializer.SerializeToElement("full")
        };
        var json = await EditorPlane.DispatchAsync(
            "cdp_editor_scene",
            store,
            session,
            new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal),
            args,
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("full", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("loci", out var loci));
        Assert.True(loci.GetArrayLength() >= 1);
    }
}
