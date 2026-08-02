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
}
