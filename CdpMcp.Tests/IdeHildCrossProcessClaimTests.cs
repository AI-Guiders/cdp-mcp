#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeHildCrossProcessClaimTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "cdp-hild-claim-" + Guid.NewGuid().ToString("N"));

    public IdeHildCrossProcessClaimTests()
    {
        Directory.CreateDirectory(_root);
        IdeHildCrossProcessClaim.RootOverrideForTests = _root;
    }

    public void Dispose()
    {
        IdeHildCrossProcessClaim.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryClaimAwayEdge_once_then_lost()
    {
        Assert.True(IdeHildCrossProcessClaim.TryClaimAwayEdge());
        Assert.False(IdeHildCrossProcessClaim.TryClaimAwayEdge());
        Assert.True(File.Exists(IdeHildCrossProcessClaim.StatePath));
    }

    [Fact]
    public void TryClaimEscalate_once_then_lost()
    {
        Assert.True(IdeHildCrossProcessClaim.TryClaimEscalate());
        Assert.False(IdeHildCrossProcessClaim.TryClaimEscalate());
    }

    [Fact]
    public void ClearAwayEpoch_allows_new_edge_and_escalate()
    {
        Assert.True(IdeHildCrossProcessClaim.TryClaimAwayEdge());
        Assert.True(IdeHildCrossProcessClaim.TryClaimEscalate());
        IdeHildCrossProcessClaim.ClearAwayEpoch();
        Assert.True(IdeHildCrossProcessClaim.TryClaimAwayEdge());
        Assert.True(IdeHildCrossProcessClaim.TryClaimEscalate());
    }

    [Fact]
    public void TryClaimAwayEdge_parallel_only_one_wins()
    {
        var wins = 0;
        Parallel.For(0, 16, _ =>
        {
            if (IdeHildCrossProcessClaim.TryClaimAwayEdge())
                Interlocked.Increment(ref wins);
        });
        Assert.Equal(1, wins);
    }

    [Fact]
    public void IsSoftDeliveredError_became_stop()
    {
        Assert.True(IdeIgniteArmHost.IsSoftDeliveredError("became_stop"));
        Assert.False(IdeIgniteArmHost.IsSoftDeliveredError("busy_timeout"));
        Assert.False(IdeIgniteArmHost.IsSoftDeliveredError(null));
    }

    [Fact]
    public void IsSystemWakeArmId_includes_stable_hild_away()
    {
        Assert.True(IdeIgniteArmHost.IsSystemWakeArmId(IdeIgniteArmHost.HildAwayArmId));
        Assert.True(IdeIgniteArmHost.IsSystemWakeArmId("hild-away-legacyguid"));
    }

    [Fact]
    public void TryScheduleHildEscalateWake_second_call_keeps_existing()
    {
        var first = IdeIgniteArmHost.TryScheduleHildEscalateWake();
        Assert.NotNull(first);
        using var d1 = JsonDocument.Parse(JsonSerializer.Serialize(first));
        var due1 = d1.RootElement.GetProperty("due_utc").GetDateTimeOffset();

        var second = IdeIgniteArmHost.TryScheduleHildEscalateWake();
        Assert.NotNull(second);
        using var d2 = JsonDocument.Parse(JsonSerializer.Serialize(second));
        var due2 = d2.RootElement.GetProperty("due_utc").GetDateTimeOffset();
        Assert.Equal(due1, due2);

        IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = JsonSerializer.SerializeToElement(IdeIgniteArmHost.HildEscalateArmId)
        });
    }
}
