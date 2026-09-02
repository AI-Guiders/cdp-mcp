using CdpMcp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpCapabilitiesRevisionTests
{
    [Fact]
    public void Bump_increments_revision()
    {
        var rev = new CdpCapabilitiesRevision();
        var a = rev.Current;
        var b = rev.Bump();
        var c = rev.Current;
        Assert.True(b > a);
        Assert.Equal(b, c);
    }

    [Fact]
    public async Task WatchAsync_yields_initial_then_bumps()
    {
        var rev = new CdpCapabilitiesRevision();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var seen = new List<long>();
        await foreach (var v in rev.WatchAsync(cts.Token))
        {
            seen.Add(v);
            if (seen.Count >= 2) break;
            if (seen.Count == 1)
                rev.Bump();
        }

        Assert.Equal(2, seen.Count);
        Assert.True(seen[1] > seen[0]);
    }

    [Fact]
    public void Distinct_runtime_instances_have_distinct_boot_revisions()
    {
        var a = new CdpCapabilitiesRevision().Current;
        var b = new CdpCapabilitiesRevision().Current;
        Assert.NotEqual(a, b);
    }
}
