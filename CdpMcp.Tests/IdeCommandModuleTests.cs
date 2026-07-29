#nullable enable
using System.Collections.Frozen;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeCommandModuleTests
{
    [Fact]
    public async Task ExecuteAsync_requires_Bind()
    {
        IdeCommandModule.Unbind();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IdeCommandModule.ExecuteAsync("cdp_health"));
    }

    [Fact]
    public async Task ExecuteAsync_forwards_command_id_and_args()
    {
        string? seenId = null;
        IReadOnlyDictionary<string, JsonElement>? seenArgs = null;
        IdeCommandModule.Bind((id, args, _) =>
        {
            seenId = id;
            seenArgs = args;
            return Task.FromResult("{\"ok\":true}");
        });
        try
        {
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("scene")
            };
            var text = await IdeCommandModule.ExecuteAsync("cdp_pressure", args);
            Assert.Equal("{\"ok\":true}", text);
            Assert.Equal("cdp_pressure", seenId);
            Assert.NotNull(seenArgs);
            Assert.True(seenArgs!.ContainsKey("op"));
            Assert.Equal("scene", seenArgs["op"].GetString());
            Assert.True(IdeCommandModule.IsBound);
        }
        finally
        {
            IdeCommandModule.Unbind();
        }
    }

    [Fact]
    public async Task ExecuteAsync_defaults_empty_args()
    {
        IReadOnlyDictionary<string, JsonElement>? seenArgs = null;
        IdeCommandModule.Bind((id, args, _) =>
        {
            seenArgs = args;
            return Task.FromResult(id);
        });
        try
        {
            var text = await IdeCommandModule.ExecuteAsync("ping");
            Assert.Equal("ping", text);
            Assert.Same(FrozenDictionary<string, JsonElement>.Empty, seenArgs);
        }
        finally
        {
            IdeCommandModule.Unbind();
        }
    }
}
