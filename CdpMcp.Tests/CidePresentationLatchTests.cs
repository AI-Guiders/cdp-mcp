using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CidePresentationLatchTests : IDisposable
{
    readonly string _root;

    public CidePresentationLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-pres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CidePresentationLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CidePresentationLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_writes_topology_latch()
    {
        CidePresentationLatch.Publish("(P)(F)(M)", CidePresentationLatch.OriginAgent);

        Assert.True(File.Exists(CidePresentationLatch.LatchPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(CidePresentationLatch.LatchPath));
        Assert.Equal(CidePresentationLatch.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("(P)(F)(M)", doc.RootElement.GetProperty("topology").GetString());
        Assert.Equal("agent", doc.RootElement.GetProperty("origin").GetString());
    }

    [Fact]
    public void TryRead_roundtrips()
    {
        CidePresentationLatch.Publish("(P+F)(M)", CidePresentationLatch.OriginAgent);
        var latch = CidePresentationLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("(P+F)(M)", latch!.Topology);
        Assert.Equal(CidePresentationLatch.OriginAgent, latch.Origin);
    }

    [Fact]
    public void Publish_instruments_and_tier_without_topology()
    {
        CidePresentationLatch.Publish(
            new CidePresentationLatch.PresentationPatch
            {
                Tier = "cockpit",
                Instruments = new Dictionary<string, string>
                {
                    ["pfd_primary"] = "workspace_map",
                    ["mfd_primary"] = "solution_explorer_tree"
                },
                MfdPage = "SolutionExplorer"
            },
            CidePresentationLatch.OriginAgent);

        var latch = CidePresentationLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Null(latch!.Topology);
        Assert.Equal("cockpit", latch.Tier);
        Assert.Equal("workspace_map", latch.Instruments!["pfd_primary"]);
        Assert.Equal("solution_explorer_tree", latch.Instruments["mfd_primary"]);
        Assert.Equal("SolutionExplorer", latch.MfdPage);
    }

    [Fact]
    public void Publish_rejects_unknown_instrument_keys()
    {
        CidePresentationLatch.Publish(
            new CidePresentationLatch.PresentationPatch
            {
                Instruments = new Dictionary<string, string> { ["nope"] = "x" }
            },
            CidePresentationLatch.OriginAgent);

        Assert.Null(CidePresentationLatch.TryRead());
    }
}
