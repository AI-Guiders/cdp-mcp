using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IgniteSerial")]
public class IdeTeethOrganTests : IDisposable
{
    readonly string _tape;
    readonly string _remountRoot;

    public IdeTeethOrganTests()
    {
        _tape = Path.Combine(Path.GetTempPath(), "cdp-teeth-tape-" + Guid.NewGuid().ToString("N") + ".jsonl");
        _remountRoot = Path.Combine(Path.GetTempPath(), "cdp-teeth-remount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_remountRoot);
        IdeTeethTape.PathOverrideForTests = _tape;
        IdeTeethTape.SuppressWriteForTests = false;
        IdeRemountWake.RootOverrideForTests = _remountRoot;
        IdeRemountWake.DefaultDueSeconds = 5;
    }

    public void Dispose()
    {
        IdeTeethTape.PathOverrideForTests = null;
        IdeRemountWake.RootOverrideForTests = null;
        IdeRemountWake.DefaultDueSeconds = 8;
        try { if (File.Exists(_tape)) File.Delete(_tape); } catch { /* ignore */ }
        try { if (Directory.Exists(_remountRoot)) Directory.Delete(_remountRoot, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Record_then_ReadTail_roundtrips()
    {
        IdeTeethTape.Record("oom_dialog", detail: "reopen_clicked");
        IdeTeethTape.Record("wake_schedule", armId: "oom-wake-test", reason: "oom", detail: "unit");

        var tail = IdeTeethTape.ReadTail(10);
        Assert.True(tail.Count >= 2);
        Assert.Equal("wake_schedule", tail[^1].Kind);
        Assert.Equal("oom", tail[^1].Reason);
        Assert.Equal("oom-wake-test", tail[^1].ArmId);
    }

    [Fact]
    public void BuildExplain_stuck_firing_busy_mentions_Stop()
    {
        IdeTeethTape.NoteGuest("stop", cdtUp: true);
        var now = new IdeTeethChannel.NowSnap(
            CdtUp: true,
            SubmitKind: "stop",
            RemountPending: false,
            OomWatch: true,
            OomClicks: 0,
            OomWakeScheduled: 1,
            LiveVersion: "0.5.339",
            Partner: "away",
            Autonomous: false,
            Arms:
            [
                new IdeTeethChannel.ArmRow(
                    "remount-wake-unit",
                    "firing",
                    "remount",
                    "remount",
                    "remount-initialized",
                    SendOk: null,
                    SendError: null,
                    SendInvokedUtc: DateTimeOffset.UtcNow,
                    Verdict: "firing")
            ],
            HildPulse: null);

        var explain = IdeTeethChannel.BuildExplain(now, []);
        Assert.Contains("busy", explain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop", explain, StringComparison.OrdinalIgnoreCase);

        var pulse = IdeTeethChannel.BuildPulse(now);
        Assert.Contains("remount=firing/busy", pulse, StringComparison.Ordinal);
        Assert.Contains("partner=away", pulse, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkPending_records_deploy_hard_on_teeth_tape()
    {
        IdeRemountWake.MarkPending(IdeDeploy.ReleaseTarget, "hard_deploy");
        var tail = IdeTeethTape.ReadTail(20);
        Assert.Contains(tail, e => e.Kind == "deploy_hard" && e.Reason == IdeRemountWake.Reason);
    }

    [Fact]
    public void TryScheduleRemount_records_wake_schedule_reason_remount()
    {
        IdeRemountWake.MarkPending(IdeDeploy.ReleaseTarget, "unit_test");
        var scheduled = IdeIgniteArmHost.TryScheduleRemountInitializedWake("cdp");
        Assert.NotNull(scheduled);

        var json = JsonSerializer.Serialize(scheduled);
        using var doc = JsonDocument.Parse(json);
        var armId = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(armId));

        var hit = IdeTeethTape.ReadTail(40)
            .LastOrDefault(e => e.Kind == "wake_schedule" && e.ArmId == armId);
        Assert.NotNull(hit);
        Assert.Equal(IdeRemountWake.Reason, hit!.Reason);

        IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(armId!)
        });
    }
}
