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
            ["m"] = "webcam_desk"
        });

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Null(latch!.MfdPage);
        Assert.Equal("agent · M: webcam", latch.ChromeHint);
    }

    [Fact]
    public void Publish_show_face_projects_face_seat_only()
    {
        CideSeatsLatch.Publish(
            new Dictionary<string, string?>
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "browser"
            },
            showFace: true,
            faceSeat: "p");

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.True(latch!.ShowFace);
        Assert.Equal("p", latch.FaceSeat);
        Assert.Null(latch.MfdPage); // plan = chrome-only; must not steal WebAiPortal from M
        Assert.Equal("agent · P: plan", latch.ChromeHint);
    }

    [Fact]
    public void Publish_show_face_git_writes_mfd()
    {
        CideSeatsLatch.Publish(
            new Dictionary<string, string?>
            {
                ["p"] = "plan",
                ["m"] = "git"
            },
            showFace: true,
            faceSeat: "m");

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.True(latch!.ShowFace);
        Assert.Equal("Git", latch.MfdPage);
        Assert.Equal("m", latch.FaceSeat);
    }

    [Fact]
    public void Publish_quiet_default_no_show_face()
    {
        CideSeatsLatch.Publish(new Dictionary<string, string?> { ["m"] = "browser" });
        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.False(latch!.ShowFace);
        Assert.Null(latch.FaceSeat);
        Assert.Equal("WebAiPortal", latch.MfdPage);
    }

    [Fact]
    public void Publish_web_ai_url_roundtrips()
    {
        CideSeatsLatch.Publish(
            new Dictionary<string, string?> { ["m"] = "browser" },
            showFace: true,
            faceSeat: "m",
            webAiUrl: "https://html.duckduckgo.com/html/?q=sierra");

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("https://html.duckduckgo.com/html/?q=sierra", latch!.WebAiUrl);
        Assert.Equal("WebAiPortal", latch.MfdPage);
        Assert.True(latch.ShowFace);

        using var doc = JsonDocument.Parse(File.ReadAllText(CideSeatsLatch.LatchPath));
        Assert.Equal(
            "https://html.duckduckgo.com/html/?q=sierra",
            doc.RootElement.GetProperty("web_ai_url").GetString());
    }

    [Fact]
    public void Publish_null_webAiUrl_preserves_prior()
    {
        CideSeatsLatch.Publish(
            new Dictionary<string, string?> { ["m"] = "browser" },
            showFace: true,
            faceSeat: "m",
            webAiUrl: "https://news.ycombinator.com");

        CideSeatsLatch.Publish(
            new Dictionary<string, string?>
            {
                ["p"] = "webcam_desk",
                ["m"] = "browser"
            },
            showFace: true,
            faceSeat: "p");

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal("https://news.ycombinator.com", latch!.WebAiUrl);
        Assert.Equal("p", latch.FaceSeat);
    }

    [Fact]
    public void Publish_empty_webAiUrl_clears()
    {
        CideSeatsLatch.Publish(
            new Dictionary<string, string?> { ["m"] = "browser" },
            showFace: true,
            faceSeat: "m",
            webAiUrl: "https://news.ycombinator.com");

        CideSeatsLatch.Publish(
            new Dictionary<string, string?> { ["m"] = "browser" },
            showFace: false,
            webAiUrl: "");

        var latch = CideSeatsLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Null(latch!.WebAiUrl);
    }
}
