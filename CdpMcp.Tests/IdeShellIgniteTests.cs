using System.Text.Json;
using TerminalMcp.Core;
using Xunit;

namespace CdpMcp.Tests;

public sealed class IdeShellIgniteTests
{
    [Fact]
    public void OnShellFinished_skips_foreground()
    {
        var before = IdeIgniteArmHost.Snapshot().Count;
        IdeShellIgnite.OnShellFinished(new ShellFinishedInfo("main", "echo hi", "C:\\", 0, Background: false, DateTimeOffset.UtcNow));
        Assert.Equal(before, IdeIgniteArmHost.Snapshot().Count);
    }

    [Fact]
    public void TryAutoArmBackground_arms_shell_finished_for_tab()
    {
        var armId = "shell-bg-test-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            Assert.True(IdeShellIgnite.TryAutoArmBackground("fremus", "python mirror.py", enabled: true, out var got));
            Assert.False(string.IsNullOrWhiteSpace(got));
            Assert.StartsWith(IdeShellIgnite.BackgroundArmIdPrefix, got!, StringComparison.Ordinal);
            var armed = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id.Equals(got, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(armed);
            Assert.Equal("shell_finished", armed!.Event);
            Assert.Equal("armed", armed.Status);
        }
        finally
        {
            _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
            {
                ["all"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }

    [Fact]
    public void Notify_background_shell_finished_fires_armed_wake()
    {
        var id = "shell-bg-notify-" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            IdeIgniteChannel.Handle(new Dictionary<string, JsonElement>
            {
                ["op"] = JsonSerializer.SerializeToElement("arm"),
                ["when"] = JsonSerializer.SerializeToElement("shell_finished"),
                ["id"] = JsonSerializer.SerializeToElement(id),
                ["task"] = JsonSerializer.SerializeToElement("notify test"),
                ["settle_seconds"] = JsonSerializer.SerializeToElement(0),
                ["ok_only"] = JsonSerializer.SerializeToElement(false),
            });
            IdeShellIgnite.OnShellFinished(new ShellFinishedInfo("notify", "python x", "C:\\", 1, Background: true, DateTimeOffset.UtcNow));
            string? status = "armed";
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                status = IdeIgniteArmHost.Snapshot().FirstOrDefault(a => a.Id == id)?.Status;
                if (status is not "armed")
                    break;
                Thread.Sleep(50);
            }

            Assert.NotEqual("armed", status);
        }
        finally
        {
            _ = IdeIgniteArmHost.Disarm(new Dictionary<string, JsonElement>
            {
                ["all"] = JsonSerializer.SerializeToElement(true),
                ["force"] = JsonSerializer.SerializeToElement(true)
            });
        }
    }
}
