#nullable enable

using CdpMcp.Habitat;
using Xunit;

namespace CdpMcp.Tests;

public sealed class HabitatNextHintTableTests
{
    static readonly Dictionary<string, NextHint[]> Rows = new(StringComparer.Ordinal)
    {
        ["a"] = [new("go1", "L1", "w1"), new("go2", "L2", "w2")],
    };

    [Fact]
    public void Resolve_known_verdict_returns_rows()
    {
        var next = NextHintTable.Resolve("a", Rows);
        Assert.Equal(2, next.Length);
    }

    [Fact]
    public void Resolve_unknown_uses_fallback()
    {
        var next = NextHintTable.Resolve("missing", Rows, [new("fb", "Fallback", "why")]);
        Assert.Single(next);
    }

    [Fact]
    public void Resolve_prefix_suffix_and_dedup()
    {
        var next = NextHintTable.Resolve(
            "a",
            Rows,
            prefix: [new("p", "Prefix", "pre")],
            suffix: [new("s", "Suffix", "post")]);
        Assert.Equal(4, next.Length);
    }

    [Fact]
    public void Dedup_drops_label_why_duplicates()
    {
        var raw = new List<object>
        {
            new { go = "x", label = "same", why = "w" },
            new { go = "y", label = "same", why = "w" },
        };
        Assert.Single(NextHintTable.Dedup(raw));
    }
}
