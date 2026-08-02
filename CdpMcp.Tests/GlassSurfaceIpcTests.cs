#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
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
