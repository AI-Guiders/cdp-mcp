using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeRemountWakeTests : IDisposable
{
    readonly string _root;

    public IdeRemountWakeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cdp-remount-wake-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IdeRemountWake.RootOverrideForTests = _root;
        IdeRemountWake.DefaultDueSeconds = 5;
    }

    public void Dispose()
    {
        IdeRemountWake.RootOverrideForTests = null;
        IdeRemountWake.DefaultDueSeconds = 8;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* temp cleanup best-effort */ }
    }

    [Fact]
    public void ComposeRemountInitializedCharge_leads_with_initialized()
    {
        var charge = IdeIgniteChannel.ComposeRemountInitializedCharge();
        Assert.Contains(IdeIgniteChannel.RemountInitializedLead, charge, StringComparison.Ordinal);
        Assert.Contains(IdeIgniteChannel.CanonicalComposerCharge, charge, StringComparison.Ordinal);
        Assert.Contains("thread amnesia", charge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkPending_then_TryConsume_roundtrips_once()
    {
        IdeRemountWake.MarkPending(IdeDeploy.ReleaseTarget, "hard_deploy");
        var path = IdeRemountWake.PendingPathForSeat("cdp");
        Assert.True(File.Exists(path));

        Assert.True(IdeRemountWake.TryConsumePending("cdp", out var pending));
        Assert.NotNull(pending);
        Assert.Equal("cdp", pending!.Seat);
        Assert.Equal("hard_deploy", pending.Reason);
        Assert.False(File.Exists(path));

        Assert.False(IdeRemountWake.TryConsumePending("cdp", out _));
    }

    [Fact]
    public void TryScheduleRemountInitializedWake_arms_remount_charge()
    {
        IdeRemountWake.MarkPending(IdeDeploy.ReleaseTarget, "unit_test");

        var scheduled = IdeIgniteArmHost.TryScheduleRemountInitializedWake("cdp");
        Assert.NotNull(scheduled);

        var json = JsonSerializer.Serialize(scheduled);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(IdeRemountWake.ChargeMode, doc.RootElement.GetProperty("charge_mode").GetString());
        Assert.Equal(IdeRemountWake.ArmTask, doc.RootElement.GetProperty("task").GetString());
        Assert.Equal("armed", doc.RootElement.GetProperty("status").GetString());
        Assert.StartsWith(IdeRemountWake.ArmIdPrefix, doc.RootElement.GetProperty("id").GetString());

        // Disarm so we do not leave a live timer in the shared host store.
        IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(doc.RootElement.GetProperty("id").GetString()!)
        });
    }
}
