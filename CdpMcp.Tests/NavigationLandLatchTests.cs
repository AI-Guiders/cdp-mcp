#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class NavigationLandLatchTests : IDisposable
{
    readonly string _root;

    public NavigationLandLatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-land-latch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        NavigationLandLatch.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        NavigationLandLatch.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Publish_writes_land_latest_json()
    {
        var path = Path.Combine(_root, "Foo.cs");
        File.WriteAllText(path, "class Foo { }");

        NavigationLandLatch.Publish("goto", path, 12, "Foo", "[Family:navigation;Command:goto]");

        Assert.True(File.Exists(NavigationLandLatch.LatchPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(NavigationLandLatch.LatchPath));
        Assert.Equal(NavigationLandLatch.Schema, doc.RootElement.GetProperty("schema").GetString());
        Assert.Equal("goto", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal(path, doc.RootElement.GetProperty("path").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("line").GetInt32());
        Assert.Equal("Foo", doc.RootElement.GetProperty("member").GetString());
    }

    [Fact]
    public void Publish_omits_non_positive_line()
    {
        NavigationLandLatch.Publish("open", "D:\\x.cs", 0, null, null);
        using var doc = JsonDocument.Parse(File.ReadAllText(NavigationLandLatch.LatchPath));
        Assert.False(doc.RootElement.TryGetProperty("line", out _));
    }
}
