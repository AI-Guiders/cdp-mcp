using Xunit;

namespace CdpMcp.Tests;

public class MetaToolCatalogIgniteTipTests
{
    [Fact]
    public void CdpIgnite_meta_teaches_habitat_prefer_and_guest_cdt_fallthrough()
    {
        var tip = MetaToolCatalog.Build().Single(t => t.Name == "cdp_ignite").Description;
        Assert.Contains("prefer habitat", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Guest CDT fallthrough", tip, StringComparison.Ordinal);
        Assert.Contains("ignite-wake-LATEST", tip, StringComparison.Ordinal);
        Assert.Contains("insurance ≠ park", tip, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoIgnition via Chrome DevTools (CDT) into Cursor Composer", tip, StringComparison.Ordinal);
    }
}
