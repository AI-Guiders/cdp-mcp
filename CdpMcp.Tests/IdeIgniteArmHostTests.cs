using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeIgniteArmHostTests
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
    public void WakeAfterHardDeploy_ok_and_reclaim_stuck_firing()
    {
        var id = "test-reclaim-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("reclaim probe"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });

        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.Status = "firing";
                a.Once = false; // recurring — reclaim mid-fire
                a.DueUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            }));

            var wake = IdeIgniteArmHost.WakeAfterHardDeploy();
            using var wdoc = JsonDocument.Parse(JsonSerializer.Serialize(wake));
            Assert.True(wdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("wake_after_hard_deploy", wdoc.RootElement.GetProperty("op").GetString());
            Assert.True(wdoc.RootElement.GetProperty("reclaimed").GetInt32() >= 1);

            var snap = IdeIgniteArmHost.Snapshot().First(a => a.Id == id);
            Assert.Equal("armed", snap.Status);
            Assert.True(snap.DueUtc is { } d && d > DateTimeOffset.UtcNow.AddSeconds(-1));
            Assert.Contains("reclaimed", snap.LastError ?? "", StringComparison.Ordinal);
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
    public void ReclaimOverdue_drops_once_stuck_firing_with_FiredUtc()
    {
        var id = "test-once-zombie-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("once zombie"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });

        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.Once = true;
                a.Status = "firing";
                a.FiredUtc = DateTimeOffset.UtcNow.AddSeconds(-30);
                a.DueUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            }));

            var reclaimed = IdeIgniteArmHost.ReclaimOverdue(TimeSpan.FromSeconds(1));
            Assert.DoesNotContain(id, reclaimed);
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Id == id);
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
    public void Hygiene_removes_error_keeps_armed()
    {
        var keep = "test-hygiene-keep-" + Guid.NewGuid().ToString("N")[..8];
        var drop = "test-hygiene-drop-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(keep),
            ["task"] = JsonSerializer.SerializeToElement("keep continuity"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(drop),
            ["task"] = JsonSerializer.SerializeToElement("stale noise"),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });

        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(drop, a =>
            {
                a.Status = "error";
                a.LastError = "fire_failed";
            }));

            var hygiene = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("hygiene")
            });
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(hygiene));
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("hygiene", doc.RootElement.GetProperty("op").GetString());

            var ids = IdeIgniteArmHost.Snapshot().Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(keep, ids);
            Assert.DoesNotContain(drop, ids);
        }
        finally
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("disarm"),
                ["id"] = JsonSerializer.SerializeToElement(keep)
            });
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("disarm"),
                ["id"] = JsonSerializer.SerializeToElement(drop)
            });
        }
    }

    [Fact]
    public void StorePath_is_seat_scoped()
    {
        Assert.Contains("ignite-arms-", IdeIgniteArmHost.StorePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "ignite-arms.json",
            IdeIgniteArmHost.StorePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(IdeIgniteArmHost.Seat));
    }

    [Fact]
    public void LastOnce_latches_awaiting_and_blocks_repeat()
    {
        var id = "test-last-once-" + Guid.NewGuid().ToString("N")[..8];
        IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
        {
            ["op"] = JsonSerializer.SerializeToElement("arm"),
            ["when"] = JsonSerializer.SerializeToElement("timer"),
            ["in"] = JsonSerializer.SerializeToElement("1h"),
            ["id"] = JsonSerializer.SerializeToElement(id),
            ["task"] = JsonSerializer.SerializeToElement("await op"),
            ["last_once"] = JsonSerializer.SerializeToElement(true),
            ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
        });

        try
        {
            Assert.True(IdeIgniteArmHost.TryMutateForTests(id, a =>
            {
                a.LastOnce = true;
                a.Once = true;
                a.Status = "awaiting";
                a.FiredUtc = DateTimeOffset.UtcNow;
            }));

            var skip = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("timer"),
                ["in"] = JsonSerializer.SerializeToElement("6s"),
                ["task"] = JsonSerializer.SerializeToElement("repeat idle"),
                ["last_once"] = JsonSerializer.SerializeToElement(true),
                ["message"] = JsonSerializer.SerializeToElement("should skip"),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0)
            });
            using var sdoc = JsonDocument.Parse(JsonSerializer.Serialize(skip));
            Assert.True(sdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(sdoc.RootElement.GetProperty("skipped").GetBoolean());
            Assert.Equal("awaiting_operator", sdoc.RootElement.GetProperty("error").GetString());

            var resume = IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("resume")
            });
            using var rdoc = JsonDocument.Parse(JsonSerializer.Serialize(resume));
            Assert.True(rdoc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, rdoc.RootElement.GetProperty("removed").GetInt32());
            Assert.DoesNotContain(IdeIgniteArmHost.Snapshot(), a => a.Id == id);
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
}
