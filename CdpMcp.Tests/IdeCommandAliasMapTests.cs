#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeCommandAliasMapTests
{
    [Theory]
    [InlineData("git_status", "git_status", true)]
    [InlineData("build", "cdp_build", false)]
    [InlineData("run_tests", "cdp_test", false)]
    [InlineData("debug_launch", "cdp_debug", false)]
    [InlineData("open_file", "cdp_buffer", false)]
    [InlineData("editor.reveal_code", "cdp_land", false)]
    public void TryResolve_bucket_A(string melodyId, string tool, bool identity)
    {
        Assert.True(IdeCommandAliasMap.TryResolve(melodyId, out var r));
        Assert.Equal(tool, r.Tool);
        Assert.Equal(identity, r.Identity);
    }

    [Fact]
    public void TryResolve_unknown_is_false()
    {
        Assert.False(IdeCommandAliasMap.TryResolve("chat_send_not_mapped", out _));
        Assert.False(IdeCommandAliasMap.TryResolve(null, out _));
    }

    [Fact]
    public void MergeArgs_caller_wins_over_defaults()
    {
        Assert.True(IdeCommandAliasMap.TryResolve("debug_launch", out var r));
        Assert.NotNull(r.Defaults);
        var caller = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("attach"),
            ["port"] = JsonSerializer.SerializeToElement(5678)
        };
        var merged = IdeCommandAliasMap.MergeArgs(r, caller);
        Assert.Equal("attach", merged["op"].GetString());
        Assert.Equal(5678, merged["port"].GetInt32());
    }

    [Fact]
    public async Task ExecuteAliasedAsync_resolves_then_forwards()
    {
        string? seen = null;
        IdeCommandModule.Bind((id, args, _) =>
        {
            seen = id + ":" + (args.TryGetValue("op", out var op) ? op.GetString() : "-");
            return Task.FromResult("ok");
        });
        try
        {
            await IdeCommandModule.ExecuteAliasedAsync("debug_continue");
            Assert.Equal("cdp_debug:continue", seen);
        }
        finally
        {
            IdeCommandModule.Unbind();
        }
    }
}
