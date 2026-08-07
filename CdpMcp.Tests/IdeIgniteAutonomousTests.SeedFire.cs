using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public partial class IdeIgniteAutonomousTests
{
    [Fact]
    public void AutonomousSeed_fire_with_incomplete_leaf_redirects_to_leaf_wake()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteArmHost.BindAutonomous(true);

        _ = IdeIgniteArmHost.AutonomousContinue("task_done_exhausted");
        IdeIgniteArmHost.BindIncompleteLeafTitleProbe(() => "Ship Cursor-dep tooth");

        Assert.True(
            IdeIgniteArmHost.TrySuppressLiveAutonomousSeedBeforeDelivery(),
            "seed fire must suppress when incomplete leaf already landed");

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(IdeIgniteArmHost.AutonomousSeedArmId, arms);
        Assert.Contains(IdeIgniteArmHost.LeafWakeArmId, arms);
    }

    [Fact]
    public void AutonomousSeed_fire_empty_board_does_not_suppress()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteArmHost.BindAutonomous(true);

        _ = IdeIgniteArmHost.AutonomousContinue("task_done_exhausted");
        IdeIgniteArmHost.BindIncompleteLeafTitleProbe(() => null);

        Assert.False(IdeIgniteArmHost.TrySuppressLiveAutonomousSeedBeforeDelivery());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IdeIgniteArmHost.AutonomousSeedArmId, arms);
    }

    [Fact]
    public void LastOnceArm_tips_under_autonomous_forbid_park_on_timer()
    {
        Assert.Contains("do not park", IdeIgniteArmHost.LastOnceArmNextStep(autonomous: true), StringComparison.Ordinal);
        Assert.Contains("NOT permission to idle", IdeIgniteArmHost.LastOnceArmHint(autonomous: true), StringComparison.Ordinal);
        Assert.Equal("end turn", IdeIgniteArmHost.LastOnceArmNextStep(autonomous: false));
        Assert.Contains("awaiting latch", IdeIgniteArmHost.LastOnceArmHint(autonomous: false), StringComparison.Ordinal);
    }

    [Fact]
    public void ArmForLeaf_when_autonomous_off_refuses_without_arming()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });

        var result = IdeIgniteArmHost.ArmForLeaf("Ship tooth", "feature_focus");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("autonomous_off", doc.RootElement.GetProperty("error").GetString());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(IdeIgniteArmHost.LeafWakeArmId, arms);

        IdeIgniteArmHost.BindAutonomous(true);
    }

    [Fact]
    public void ArmForLeafHint_under_autonomous_does_not_teach_end_turn_park()
    {
        var auto = IdeIgniteArmHost.ArmForLeafHint(autonomous: true);
        Assert.Contains("Keep flying", auto, StringComparison.Ordinal);
        Assert.Contains("not a license to park", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("End turn", auto, StringComparison.Ordinal);

        var partner = IdeIgniteArmHost.ArmForLeafHint(autonomous: false);
        Assert.Contains("End turn", partner, StringComparison.Ordinal);

        var invent = IdeIgniteArmHost.ArmForLeafHint(autonomous: true, inventOnlyHold: true);
        Assert.Contains("15m invent-only", invent, StringComparison.Ordinal);
        Assert.Contains("DIG REJECT", invent, StringComparison.Ordinal);
        Assert.DoesNotContain("End turn", invent, StringComparison.Ordinal);
    }

        [Fact]
    public void ArmForLeaf_invent_only_hold_uses_15m_last_once()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteArmHost.BindAutonomous(false);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteArmHost.BindAutonomous(true);

        var result = IdeIgniteArmHost.ArmForLeaf(
            "Hold Citizen Done stable to 15.08 — invent only on real product gap",
            "invent_only_softener");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("invent_only_hold").GetBoolean());
        Assert.Equal("15m", doc.RootElement.GetProperty("in_raw").GetString());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var leaf = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .First(a => a.GetProperty("id").GetString() == IdeIgniteArmHost.LeafWakeArmId);
        Assert.Equal("15m", leaf.GetProperty("in_raw").GetString());
        Assert.True(leaf.GetProperty("last_once").GetBoolean());
    }

    [Fact]
    public void ContinuityArmedNextStep_under_autonomous_forbids_wait_for_event_park()
    {
        var auto = IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true);
        Assert.Contains("keep flying", auto, StringComparison.Ordinal);
        Assert.Contains("do not park", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("wait for event", auto, StringComparison.Ordinal);

        Assert.Equal("wait for event", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: false));
    }

    [Fact]
    public void PressureTips_under_autonomous_forbid_end_turn_park()
    {
        var check = IdePressureChannel.AutoIgnitionChecklistLine(autonomous: true);
        Assert.Contains("keep flying", check, StringComparison.Ordinal);
        Assert.Contains("insurance", check, StringComparison.Ordinal);
        Assert.DoesNotContain("end turn", check, StringComparison.OrdinalIgnoreCase);

        var scene = IdePressureChannel.SceneArmedHint(autonomous: true);
        Assert.Contains("not a nap", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("before end turn", scene, StringComparison.OrdinalIgnoreCase);

        var stash = IdePressureChannel.StashHint(autonomous: true);
        Assert.Contains("do not park", stash, StringComparison.Ordinal);
        Assert.DoesNotContain("ending turn", stash, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("before end turn", IdePressureChannel.AutoIgnitionChecklistLine(autonomous: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before end turn", IdePressureChannel.SceneArmedHint(autonomous: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ending turn", IdePressureChannel.StashHint(autonomous: false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContinuityArmedNextStep_used_for_non_last_once_explain_under_autonomous()
    {
        // ArmPath event arms: autonomous must not teach wait-for-event / end-turn park.
        Assert.DoesNotContain("wait for event", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true), StringComparison.Ordinal);
        Assert.Contains("keep flying", IdeIgniteArmHost.ContinuityArmedNextStep(autonomous: true), StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalComposerCharge_does_not_teach_rearm_when_idle()
    {
        Assert.Contains("timer ≠ idle license", IdeIgniteChannel.CanonicalComposerCharge, StringComparison.Ordinal);
        Assert.DoesNotContain("re-arm when idle", IdeIgniteChannel.CanonicalComposerCharge, StringComparison.Ordinal);
    }

}
