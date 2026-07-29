using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CideSeatsLatchTests : IDisposable
{
    readonly string _root;

    public CideSeatsLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-seats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        CideSeatsLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CideSeatsLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Publish_writes_shell_mfd_from_m_seat()
    {
        CideSeatsLatch.Publish(new Dictionary<string, string?>
        {
            ["p"] = "plan",
            ["forward"] = "editor_scene",
            ["m"] = "shell"
        });

        Assert.True(File.Exists(CideSeatsLatch.LatchPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(CideSeatsLatch.LatchPath));
        Assert.Equal(CideSeatsLatch.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Terminal", doc.RootElement.GetProperty("mfd_page").GetString());
        Assert.Equal("shell", doc.RootElement.GetProperty("seats").GetProperty("m").GetString());
        Assert.Equal("agent", doc.RootElement.GetProperty("origin").GetString());
    }

    [Fact]
    public void Publish_pressure_chrome_without_mfd()
    {
        CideSeatsLatch.Publish(new Dictionary<string, string?>
        {
            ["m"] = "pressure_desk"
        });

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Null(latch!.MfdPage);
        Assert.Equal("agent · M: pressure", latch.ChromeHint);
    }

    [Fact]
    public void Publish_unmapped_m_gets_name_chrome()
    {
        CideSeatsLatch.Publish(new Dictionary<string, string?>
        {
            ["m"] = "arch_desk"
        });

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Null(latch!.MfdPage);
        Assert.Equal("agent · M: arch_desk", latch.ChromeHint);
    }

    [Fact]
    public void TryRead_roundtrips()
    {
        CideSeatsLatch.Publish(new Dictionary<string, string?> { ["m"] = "browser" });
        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("WebAiPortal", latch!.MfdPage);
        Assert.Equal("browser", latch.Seats!["m"]);
    }
}
