using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeShareTests
{
    [Fact]
    public void ResolveShareInbox_prefers_project_dot_cdp_share()
    {
        var root = Path.Combine(Path.GetTempPath(), "cdp-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var inbox = IdeShare.ResolveShareInbox(root, null);
            Assert.Equal(Path.Combine(root, ".cdp", "share"), inbox);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Repl_share_buffer_routes_go_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share with operator", empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("share", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal(JsonValueKind.Object, ga.ValueKind);
        Assert.Equal("operator", ga.GetProperty("with").GetString());
        Assert.Equal("buffer", ga.GetProperty("what").GetString());
    }

    [Fact]
    public void Repl_share_plan_routes_tm_share()
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var applied = IdeRepl.Apply("share plan ask=confirm", empty);
        Assert.NotNull(applied);
        Assert.Null(applied.Value.Direct);
        Assert.True(applied.Value.Args.TryGetValue("go", out var go));
        Assert.Equal("plan", go.GetString());
        Assert.True(applied.Value.Args.TryGetValue("tm_op", out var tm));
        Assert.Equal("share", tm.GetString());
        Assert.True(applied.Value.Args.TryGetValue("go_args", out var ga));
        Assert.Equal("plan", ga.GetProperty("what").GetString());
        Assert.Equal("confirm", ga.GetProperty("ask").GetString());
    }
}
