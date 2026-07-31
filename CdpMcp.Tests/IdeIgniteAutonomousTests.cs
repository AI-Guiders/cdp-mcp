using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteAutonomousTests : IDisposable
{
    public IdeIgniteAutonomousTests()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.Fly);
        IdeIgniteArmHost.BindAutonomous(null);
    }

    public void Dispose()
    {
        IdeIgniteArmHost.BindAutonomous(null);
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.Fly);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["id"] = JsonSerializer.SerializeToElement(IdeIgniteArmHost.AutonomousSeedArmId)
        });
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("resume")
        });
    }

    [Fact]
    public void AutonomousContinue_when_armed_does_not_latch_await_operator()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        var result = IdeIgniteArmHost.AutonomousContinue("task_done_exhausted");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.False(root.GetProperty("plateau").GetBoolean());
        Assert.True(root.GetProperty("autonomous").GetBoolean());
        Assert.True(root.GetProperty("need_seed").GetBoolean());
        Assert.Equal("autonomous_continue", root.GetProperty("op").GetString());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        Assert.True(listDoc.RootElement.GetProperty("continuity").GetProperty("autonomous").GetBoolean());
        Assert.False(listDoc.RootElement.GetProperty("continuity").GetProperty("await_operator").GetBoolean());
    }

    [Fact]
    public void AwaitOperator_explicit_still_latches_even_when_autonomous_armed()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("await_operator"),
            ["task"] = JsonSerializer.SerializeToElement("explicit park")
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("epic_closed").GetBoolean());
        Assert.Equal("awaiting", doc.RootElement.GetProperty("arm").GetProperty("status").GetString());
    }

    [Fact]
    public void Autonomous_off_op_sets_latch_false()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("autonomous_off")
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("armed").GetBoolean());

        // restore default for other tests / dogfood
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("autonomous_on")
        });
        IdeIgniteArmHost.BindAutonomous(null);
    }

    [Fact]
    public void Disarm_all_under_autonomous_keeps_seed_and_reseeds_if_empty()
    {
        IdeIgniteArmHost.BindAutonomous(true);

        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("30m"),
            ["task"] = JsonSerializer.SerializeToElement("work leaf"),
            ["id"] = JsonSerializer.SerializeToElement("work-arm-1"),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("30m"),
            ["task"] = JsonSerializer.SerializeToElement("Autonomous continuity — seed next leaf"),
            ["id"] = JsonSerializer.SerializeToElement(IdeIgniteArmHost.AutonomousSeedArmId),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });

        var disarm = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true)
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(disarm));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("except_autonomy").GetBoolean());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        var arms = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IdeIgniteArmHost.AutonomousSeedArmId, arms);
        Assert.DoesNotContain("work-arm-1", arms);
        Assert.True(listDoc.RootElement.GetProperty("continuity").GetProperty("armed").GetInt32() >= 1);
    }

    [Fact]
    public void Disarm_all_force_under_autonomous_still_reseeds()
    {
        IdeIgniteArmHost.BindAutonomous(true);
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("30m"),
            ["task"] = JsonSerializer.SerializeToElement("work"),
            ["id"] = JsonSerializer.SerializeToElement("work-arm-force"),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });

        var disarm = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["all"] = JsonSerializer.SerializeToElement(true),
            ["force"] = JsonSerializer.SerializeToElement(true)
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(disarm));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("except_autonomy").GetBoolean());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        using var listDoc = JsonDocument.Parse(JsonSerializer.Serialize(list));
        Assert.True(listDoc.RootElement.GetProperty("continuity").GetProperty("armed").GetInt32() >= 1);
        var ids = listDoc.RootElement.GetProperty("arms").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(IdeIgniteArmHost.AutonomousSeedArmId, ids);
    }
}
