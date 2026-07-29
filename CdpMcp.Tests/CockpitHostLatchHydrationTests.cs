#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class CockpitHostLatchHydrationTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "cdp-hydrate-" + Guid.NewGuid().ToString("N"));

    public CockpitHostLatchHydrationTests()
    {
        Directory.CreateDirectory(_root);
        CockpitHostLatchHydration.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        CockpitHostLatchHydration.RootOverrideForTests = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* temp cleanup best-effort */
        }
    }

    [Fact]
    public void Touch_bumps_stamped_utc_on_existing_latches()
    {
        var path = Path.Combine(_root, "seats-LATEST.json");
        File.WriteAllText(path, """
            {"schema":"cide_seats_latch/v1","seats":{"m":"shell_scene"},"stamped_utc":"2020-01-01T00:00:00.0000000+00:00"}
            """.Trim());

        Assert.Equal(1, CockpitHostLatchHydration.TouchAgentLatchesForHostStart());

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var stamp = doc.RootElement.GetProperty("stamped_utc").GetString();
        Assert.False(string.IsNullOrWhiteSpace(stamp));
        Assert.NotEqual("2020-01-01T00:00:00.0000000+00:00", stamp);
        Assert.Equal("shell_scene", doc.RootElement.GetProperty("seats").GetProperty("m").GetString());
    }

    [Fact]
    public void Touch_skips_missing_files()
    {
        Assert.Equal(0, CockpitHostLatchHydration.TouchAgentLatchesForHostStart());
    }
}
