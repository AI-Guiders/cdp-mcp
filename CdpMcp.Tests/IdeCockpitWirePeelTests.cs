#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeCockpitWirePeelTests
{
    [Fact]
    public void IngestCockpitRequest_captures_cmd_and_source()
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["cmd"] = JsonSerializer.SerializeToElement("go sa"),
            ["transport_source"] = JsonSerializer.SerializeToElement("test")
        };
        var env = IdeCockpit.IngestCockpitRequest(args);
        Assert.Equal("go sa", env.CmdLine);
        Assert.Equal("test", env.Source);
        Assert.NotEqual(default, env.IngestedUtc);
    }

    [Fact]
    public void DescribeSeatsInstrumentDeck_returns_named_deck()
    {
        var deck = IdeCockpit.DescribeSeatsInstrumentDeck();
        Assert.True(deck.DeckId is "desk-tiles" or "desk-seats", deck.DeckId);
        Assert.False(string.IsNullOrWhiteSpace(deck.SemanticAnchorId));
        Assert.NotNull(deck.Instruments);
    }

    [Fact]
    public void InstrumentPulse_exposes_peel_seam()
    {
        var json = JsonSerializer.Serialize(IdeCockpit.InstrumentPulse());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("instrument", doc.RootElement.GetProperty("seam").GetString());
        Assert.True(doc.RootElement.GetProperty("peel").GetBoolean());
    }
}
