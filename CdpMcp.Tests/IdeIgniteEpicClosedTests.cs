using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IgniteSerial")]
public class IdeIgniteEpicClosedTests : IDisposable
{
    public IdeIgniteEpicClosedTests()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.Fly);
    }

    public void Dispose()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.Fly);
    }

    [Fact]
    public void LastOnce_on_handoff_latches_awaiting_without_due()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.EpicClosedHandoff);
        var id = "test-epic-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("12m"),
                ["task"] = JsonSerializer.SerializeToElement("would invent next epic"),
                ["last_once"] = JsonSerializer.SerializeToElement(true),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.True(root.GetProperty("epic_closed").GetBoolean());
            Assert.Equal("epic_closed", root.GetProperty("error").GetString());
            Assert.Equal("awaiting_partner", root.GetProperty("continuity").GetString());
            Assert.Equal("focus_handoff", root.GetProperty("reason").GetString());
            Assert.Equal("awaiting", root.GetProperty("arm").GetProperty("status").GetString());
            Assert.True(!root.GetProperty("arm").TryGetProperty("due_utc", out var due)
                        || due.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("resume")
            });
        }
    }

    [Fact]
    public void LastOnce_epic_closed_allows_force_override()
    {
        IdeIgniteArmHost.BindFlightProbe(() => ContinuityFlight.EpicClosedNoAct);
        var id = "test-force-epic-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1m"),
                ["task"] = JsonSerializer.SerializeToElement("forced continuity"),
                ["last_once"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(doc.RootElement.TryGetProperty("epic_closed", out var ec) && ec.GetBoolean());
            Assert.Equal("armed", doc.RootElement.GetProperty("arm").GetProperty("status").GetString());
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("disarm"),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
        }
    }

    [Fact]
    public void AwaitOperator_op_latches_without_timer()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("await_operator"),
            ["task"] = JsonSerializer.SerializeToElement("park plateau")
        });
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("epic_closed").GetBoolean());
            Assert.Equal("awaiting", doc.RootElement.GetProperty("arm").GetProperty("status").GetString());
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("resume")
            });
        }
    }

    [Theory]
    [InlineData("Ship peels complete @handoff #CDP", "handoff")]
    [InlineData("Wire CRM @act", "act")]
    [InlineData("plain title", null)]
    public void ResolvePhaseWire_reads_title_tag(string title, string? expect)
    {
        Assert.Equal(expect, IdeTaskManager.ResolvePhaseWire(null, title));
    }
}
