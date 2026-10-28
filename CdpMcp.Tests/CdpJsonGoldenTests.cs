#nullable enable
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>
/// Golden conformance for the JSON serialization SSOT (CdpJson.cs + bridge mirror):
/// non-ASCII round-trips unescaped through both writer paths.
/// The sweep lives in CdpAnalyzers (CDPJ001/CDPJ002) since 2026-10-28.
/// </summary>
public sealed class CdpJsonGoldenTests
{
    [Fact]
    public void Golden_non_ascii_round_trips_unescaped()
    {
        const string sample = "привет → среда · CRM cross-check · ✈";
        var indented = JsonSerializer.Serialize(new { channel = sample }, CdpJson.Indented);
        var compact = JsonSerializer.Serialize(new { channel = sample }, CdpJson.Compact);
        Assert.DoesNotContain("\\u", indented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sample, indented, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", compact, StringComparison.OrdinalIgnoreCase);

        var utf8 = new MemoryStream();
        using (var writer = new Utf8JsonWriter(utf8, CdpJson.WriterOptionsIndented))
        {
            writer.WriteStartObject();
            writer.WriteString("channel", sample);
            writer.WriteEndObject();
        }

        var wire = System.Text.Encoding.UTF8.GetString(utf8.ToArray());
        Assert.DoesNotContain("\\u", wire, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sample, wire, StringComparison.Ordinal);
    }
}
