using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

[Collection("IgniteSerial")]
public partial class IdeIgniteArmHostTests
{
    [Theory]
    [InlineData("provider_blocked", true)]
    [InlineData("busy_timeout", false)]
    [InlineData("fire_failed", false)]
    public void ShouldEnterProviderBlockedContinuity_policy(string err, bool expect) =>
        Assert.Equal(expect, IdeIgniteArmHost.ShouldEnterProviderBlockedContinuity(err));

    [Fact]
    public void ProviderBlockedStatus_is_distinct_from_awaiting() =>
        Assert.NotEqual("awaiting", IdeIgniteArmHost.ProviderBlockedStatus);

    [Fact]
    public void NormalizeEvent_maps_aliases()
    {
        Assert.Equal("build_finished", IdeIgniteArmHost.NormalizeEvent("build"));
        Assert.Equal("test_finished", IdeIgniteArmHost.NormalizeEvent("tests"));
        Assert.Equal("timer", IdeIgniteArmHost.NormalizeEvent("delay"));
        Assert.Equal("timer", IdeIgniteArmHost.NormalizeEvent("timer"));
        Assert.Equal("shell_finished", IdeIgniteArmHost.NormalizeEvent("shell"));
    }

    [Theory]
    [InlineData("timer", "busy_timeout", true)]
    [InlineData("timer", "fire_failed", false)]
    [InlineData("build_finished", "busy_timeout", false)]
    [InlineData("TIMER", "busy_timeout", true)]
    public void ShouldRequeueBusy_policy(string ev, string err, bool expect) =>
        Assert.Equal(expect, IdeIgniteArmHost.ShouldRequeueBusy(ev, err));

    [Theory]
    [InlineData(false, true, true)]  // last_once → keep error visible
    [InlineData(true, true, true)]   // last_once implies once → still keep
    [InlineData(true, false, false)] // plain once → silent Remove ok
    [InlineData(false, false, true)] // recurring → keep error
    public void ShouldKeepVisibleErrorOnFireFail_policy(bool once, bool lastOnce, bool expect) =>
        Assert.Equal(expect, IdeIgniteArmHost.ShouldKeepVisibleErrorOnFireFail(once, lastOnce));

    [Theory]
    [InlineData(90, 30)]
    [InlineData(5, 15)]
    [InlineData(600, 60)]
    public void BusyBackoff_clamps(int wait, int seconds) =>
        Assert.Equal(seconds, (int)IdeIgniteArmHost.BusyBackoff(wait).TotalSeconds);

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    public void TryParseDuration_ok(string raw, int seconds)
    {
        Assert.True(IdeIgniteArmHost.TryParseDuration(raw, out var span));
        Assert.Equal(seconds, (int)span.TotalSeconds);
    }

    [Fact]
    public void Arm_timer_without_cdt_persists()
    {
        var id = "test-arm-" + Guid.NewGuid().ToString("N")[..8];
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("unit-test next"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("arm", doc.RootElement.GetProperty("op").GetString());

        var list = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        });
        var listJson = JsonSerializer.Serialize(list);
        Assert.Contains(id, listJson, StringComparison.Ordinal);

        var disarm = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["id"] = JsonSerializer.SerializeToElement(id)
        });
        using var ddoc = JsonDocument.Parse(JsonSerializer.Serialize(disarm));
        Assert.True(ddoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, ddoc.RootElement.GetProperty("removed").GetInt32());
    }

    [Fact]
    public void Arm_with_task_only_stores_canonical_message()
    {
        var id = "test-canonical-" + Guid.NewGuid().ToString("N")[..8];
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("2h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("Full-ready digest stage"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var listJson = JsonSerializer.Serialize(IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("list")
        }));
        Assert.Contains(IdeIgniteChannel.CanonicalComposerCharge, listJson, StringComparison.Ordinal);
        Assert.Contains("Full-ready digest stage", listJson, StringComparison.Ordinal);

        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("disarm"),
            ["id"] = JsonSerializer.SerializeToElement(id)
        });
    }

    [Fact]
    public void Arm_requires_message_or_task()
    {
        var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1m")
        });
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("message_or_task_required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void LastOnce_on_plateau_requires_active_task_focus()
    {
        IdeIgniteArmHost.BindTaskFocus(() => false);
        try
        {
            var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1m"),
                ["task"] = JsonSerializer.SerializeToElement("plateau probe"),
                ["last_once"] = JsonSerializer.SerializeToElement(true)
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("no_active_task", doc.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            IdeIgniteArmHost.BindTaskFocus(() => true);
        }
    }

    [Fact]
    public void LastOnce_on_plateau_allows_explicit_force_override()
    {
        var id = "test-force-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteArmHost.BindTaskFocus(() => false);
        try
        {
            var result = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("1m"),
                ["task"] = JsonSerializer.SerializeToElement("plateau override"),
                ["last_once"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            IdeIgniteArmHost.BindTaskFocus(() => true);
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("disarm"),
                ["id"] = JsonSerializer.SerializeToElement(id)
            });
        }
    }

}
