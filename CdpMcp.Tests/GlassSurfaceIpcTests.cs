#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cdp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class GlassSurfaceIpcTests : IDisposable
{
    readonly string _root;

    public GlassSurfaceIpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-surface-ipc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        GlassSurfaceIpc.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        GlassSurfaceIpc.RootOverrideForTests = null;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* temp */
        }
    }

    [Fact]
    public void Call_layout_matches_reply_id()
    {
        var replyTask = Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                if (!File.Exists(GlassSurfaceIpc.CmdPath))
                {
                    Thread.Sleep(20);
                    continue;
                }

                using var cmdDoc = JsonDocument.Parse(File.ReadAllText(GlassSurfaceIpc.CmdPath));
                var id = cmdDoc.RootElement.GetProperty("id").GetString();
                var reply = new JsonObject
                {
                    ["schema"] = "agent_surface/v0",
                    ["id"] = id,
                    ["ok"] = true,
                    ["op"] = "layout",
                    ["result"] = new JsonObject
                    {
                        ["schema"] = "agent_surface/v0",
                        ["windows"] = new JsonArray
                        {
                            new JsonObject { ["role"] = "main", ["title"] = "test" }
                        }
                    }
                };
                var tmp = GlassSurfaceIpc.ReplyPath + ".tmp";
                File.WriteAllText(tmp, reply.ToJsonString());
                File.Move(tmp, GlassSurfaceIpc.ReplyPath, overwrite: true);
                return;
            }
        });

        var (ok, replyEl, error) = GlassSurfaceIpc.Call("layout", args: null, timeoutMs: 3000);
        replyTask.Wait(TimeSpan.FromSeconds(4));
        Assert.True(ok, error);
        Assert.NotNull(replyEl);
        Assert.Equal("layout", replyEl!.Value.GetProperty("op").GetString());
        Assert.True(replyEl.Value.GetProperty("result").GetProperty("windows").GetArrayLength() >= 1);
    }

    [Fact]
    public void Call_times_out_without_reply()
    {
        var (ok, _, error) = GlassSurfaceIpc.Call("layout", args: null, timeoutMs: 200);
        Assert.False(ok);
        Assert.Equal("surface_timeout", error);
    }
}

public sealed class IdeGlassSurfaceChannelTests
{
    [Fact]
    public void Scene_lists_run_and_palette()
    {
        var json = IdeGlassSurfaceChannel.HandleJson(new SessionContext(), null);
        using var doc = JsonDocument.Parse(json);
        var set = doc.RootElement.GetProperty("implemented")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("run", set);
        Assert.Contains("palette", set);
        Assert.Contains("action", set);
    }

    [Fact]
    public void Scene_includes_shared_ssot_shape()
    {
        var json = IdeGlassSurfaceChannel.HandleJson(new SessionContext(), null);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("shared_ssot", out var ssot));
        Assert.True(ssot.TryGetProperty("next", out _));
        Assert.True(ssot.TryGetProperty("why", out _));
        Assert.True(ssot.TryGetProperty("active", out _));
        Assert.True(ssot.TryGetProperty("file_situ", out var situ));
        Assert.True(situ.TryGetProperty("path", out _));
        Assert.True(situ.TryGetProperty("why_this_file", out _));
        Assert.True(situ.TryGetProperty("blast", out var blast));
        Assert.Equal(JsonValueKind.Array, blast.ValueKind);
        Assert.True(situ.TryGetProperty("role_in_graph", out _));
    }

    [Fact]
    public void Scene_includes_cabin_sa_omnibus()
    {
        var json = IdeGlassSurfaceChannel.HandleJson(new SessionContext(), null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("glass_scene", doc.RootElement.GetProperty("go_alias").GetString());
        Assert.True(doc.RootElement.TryGetProperty("cabin", out var cabin));
        Assert.Equal("cabin_sa/v0", cabin.GetProperty("schema").GetString());
        Assert.True(cabin.TryGetProperty("why", out _));
        Assert.True(cabin.TryGetProperty("next", out _));
        Assert.True(cabin.TryGetProperty("course", out _));
        Assert.True(cabin.TryGetProperty("seats", out _));
        Assert.True(cabin.TryGetProperty("mfd_page", out _));
        Assert.True(cabin.TryGetProperty("land", out _));
        Assert.True(cabin.TryGetProperty("shared", out _));
        Assert.True(cabin.TryGetProperty("ignite", out _));
        Assert.True(cabin.TryGetProperty("alert", out _));
        Assert.True(cabin.TryGetProperty("file_situ", out var situ));
        Assert.True(situ.TryGetProperty("applies_on_locus", out _));
        Assert.Contains("glass_scene", doc.RootElement.GetProperty("pulse").GetString(), StringComparison.OrdinalIgnoreCase);
        var diff = situ.GetProperty("diff_intent");
        Assert.Equal("DIFF · on demand", diff.GetProperty("line").GetString());
    }
}
