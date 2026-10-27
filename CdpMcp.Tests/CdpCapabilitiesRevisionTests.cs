using CdpMcp;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CdpCapabilitiesRevisionTests
{
    [Fact]
    public void Bump_increments_revision()
    {
        var rev = new CdpCapabilitiesRevision(42);
        var a = rev.Current;
        var b = rev.Bump();
        var c = rev.Current;
        Assert.True(b > a);
        Assert.Equal(b, c);
    }

    [Fact]
    public async Task WatchAsync_yields_initial_then_bumps()
    {
        var rev = new CdpCapabilitiesRevision(42);
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
    public void Same_epoch_same_revision_identical_catalog_across_instances()
    {
        // Контракт (2026-10-27): rev идентичен по содержимому каталога —
        // два процесса с одинаковым каталогом дают одинаковый rev, кэши
        // бриджа не инвалидируются зря (REV-STUCK фикс).
        var a = new CdpCapabilitiesRevision(100).Current;
        var b = new CdpCapabilitiesRevision(100).Current;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_catalog_different_epoch_different_revision()
    {
        var a = new CdpCapabilitiesRevision(100).Current;
        var b = new CdpCapabilitiesRevision(200).Current;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Default_epoch_differs_from_legacy_constant_base()
    {
        // Старый баг: rev всегда 4294967297 ((boot=1)<<32 | 0). Контентный
        // epoch не должен совпадать с этой константой.
        var rev = new CdpCapabilitiesRevision().Current;
        Assert.NotEqual(4294967297L, rev);
    }
}
