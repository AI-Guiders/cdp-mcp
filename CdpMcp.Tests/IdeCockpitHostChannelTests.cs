#nullable enable
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

public class IdeCockpitHostChannelTests
{
    [Fact]
    public void Scene_defaults_agent_only_down()
    {
        // Ensure clean state for this process profile root
        _ = IdeCockpitHostChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("stop")
        });

        using var doc = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson());
        Assert.Equal("down", doc.RootElement.GetProperty("gui_host").GetString());
        Assert.Equal("agent-only", doc.RootElement.GetProperty("host_profile").GetString());
    }

    [Fact]
    public void Start_without_exe_fails_clearly()
    {
        var prev = Environment.GetEnvironmentVariable(IdeCockpitHostChannel.EnvExe);
        try
        {
            Environment.SetEnvironmentVariable(IdeCockpitHostChannel.EnvExe, null);
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("start")
            };
            using var doc = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(args));
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains("not configured", doc.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdeCockpitHostChannel.EnvExe, prev);
        }
    }

    [Fact]
    public void Start_and_stop_lifecycle_with_notepad()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(notepad))
            return; // non-Windows CI skip

        var argsStart = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("start"),
            ["path"] = JsonSerializer.SerializeToElement(notepad)
        };
        using (var started = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(argsStart)))
        {
            Assert.True(started.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("up", started.RootElement.GetProperty("gui_host").GetString());
            Assert.True(started.RootElement.GetProperty("pid").GetInt32() > 0);
        }

        using var stopped = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stop")
            }));
        Assert.True(stopped.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("down", stopped.RootElement.GetProperty("gui_host").GetString());
        Assert.Equal("agent-only", stopped.RootElement.GetProperty("host_profile").GetString());
    }
}
