#nullable enable

using CdpMcp.Habitat;
using Xunit;

namespace CdpMcp.Tests;

public sealed class HabitatPrefixOpTableTests
{
    static readonly PrefixOpRule[] Rules =
    [
        new("clear", "clipboard_clear", "clip_clear"),
        new("clip", "clipboard", "clip"),
    ];

    [Fact]
    public void Match_specific_prefix_before_general()
    {
        Assert.Equal("clear", PrefixOpTable.Match("clipboard_clear", Rules));
        Assert.Equal("clip", PrefixOpTable.Match("clipboard", Rules));
    }

    [Fact]
    public void MatchSubcommand_empty_returns_whenEmpty()
    {
        Assert.Equal("scene", PrefixOpTable.MatchSubcommand("buffer", "buffer", Rules, whenEmpty: "scene"));
    }

    [Fact]
    public void Normalize_alias_map()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["invoke"] = "call" };
        Assert.Equal("call", PrefixOpTable.Normalize("invoke", aliases));
    }
}
