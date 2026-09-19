#nullable enable
using Xunit;

namespace CdpMcp.Tests;

public class GraphQlCollapseListToolsTests
{
    [Fact]
    public void Collapsed_read_names_cover_plan_bare_and_meta()
    {
        Assert.Contains("find_in_files", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.Contains("cdp_peek", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.Contains("cdp_search", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.Contains("get_diagnostics", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.DoesNotContain("cdp_graphql", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.DoesNotContain("rename_symbol", VisibleToolCatalog.GraphQlCollapsedReadNames);
        Assert.DoesNotContain("get_completions", VisibleToolCatalog.GraphQlCollapsedReadNames);
    }

    [Fact]
    public void Meta_catalog_still_defines_collapsed_tools_for_CallTool()
    {
        var meta = MetaToolCatalog.Build();
        Assert.Contains(meta, t => t.Name == "cdp_peek");
        Assert.Contains(meta, t => t.Name == "cdp_graphql");
    }
}
