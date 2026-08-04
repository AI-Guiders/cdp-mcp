#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeSeeChannelTests
{
    // 1×1 PNG
    static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Scene_pulse_ok()
    {
        ToolMediaOutbox.Clear();
        var json = IdeSeeChannel.HandleJson(new SessionContext(), Args(("op", "scene")));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(IdeSeeChannel.Schema, doc.RootElement.GetProperty("schema").GetString());
    }

    [Fact]
    public void Path_attaches_ImageContent()
    {
        ToolMediaOutbox.Clear();
        CitizenVisionLatch.ResetForTests();
        var dir = Path.Combine(Path.GetTempPath(), "cdp-see-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var png = Path.Combine(dir, "tiny.png");
            File.WriteAllBytes(png, TinyPng);
            var session = new SessionContext { ProjectRoot = dir };
            var json = IdeSeeChannel.HandleJson(session, Args(("path", png)));
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("attached_image").GetBoolean());
            Assert.Equal("image/png", doc.RootElement.GetProperty("mime").GetString());
            Assert.True(doc.RootElement.GetProperty("citizen_vision_latched").GetBoolean());

            var blocks = ToolMediaOutbox.BuildContent("see-test");
            Assert.Equal(2, blocks.Count); // text + image
            Assert.NotNull(CitizenVisionLatch.Peek());
        }
        finally
        {
            ToolMediaOutbox.Clear();
            CitizenVisionLatch.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Missing_path_and_url_errors()
    {
        ToolMediaOutbox.Clear();
        var json = IdeSeeChannel.HandleJson(new SessionContext(), null);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("path_or_url_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Relative_path_under_ProjectRoot()
    {
        ToolMediaOutbox.Clear();
        CitizenVisionLatch.ResetForTests();
        var dir = Path.Combine(Path.GetTempPath(), "cdp-see-rel-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "rel.png"), TinyPng);
            var session = new SessionContext { ProjectRoot = dir };
            var json = IdeSeeChannel.HandleJson(session, Args(("file", "rel.png")));
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("attached_image").GetBoolean());
        }
        finally
        {
            ToolMediaOutbox.Clear();
            CitizenVisionLatch.ResetForTests();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    static Dictionary<string, JsonElement> Args(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
            d[k] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
