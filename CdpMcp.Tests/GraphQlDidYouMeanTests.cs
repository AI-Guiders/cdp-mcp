#nullable enable
using CdpMcp.GraphQl;
using Xunit;

namespace CdpMcp.Tests;

public class GraphQlDidYouMeanTests
{
    [Fact]
    public void Suggest_orders_textHits_near_typo()
    {
        var s = GraphQlUnknownFieldErgonomics.Suggest("The field `textHitss` does not exist on the type `Query`.");
        Assert.Equal("textHits", s[0]);
    }

    [Fact]
    public void Catalog_lists_L2_roots()
    {
        Assert.Contains("textHits", GraphQlUnknownFieldErgonomics.QueryFieldCatalog);
        Assert.Contains("knowledge", GraphQlUnknownFieldErgonomics.QueryFieldCatalog);
        Assert.Contains("peek", GraphQlUnknownFieldErgonomics.QueryFieldCatalog);
    }

    [Fact]
    public void EnrichMcpErrors_adds_didYouMean_and_availableFields()
    {
        const string raw = """
            {"errors":[{"message":"The field `textHitss` does not exist on the type `Query`.","extensions":{"type":"Query","field":"textHitss"}}]}
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var enriched = GraphQlUnknownFieldErgonomics.EnrichMcpErrors(doc.RootElement);
        var ext = enriched.GetProperty("errors")[0].GetProperty("extensions");
        Assert.Equal("textHits", ext.GetProperty("didYouMean")[0].GetString());
        Assert.Contains(ext.GetProperty("availableFields").EnumerateArray(), e => e.GetString() == "peek");
        Assert.True(ext.TryGetProperty("hint", out _));
    }
}
