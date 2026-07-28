#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;

/// <summary>Serial — IdeCockpitHostChannel is process-static.</summary>
[CollectionDefinition(nameof(IdeCockpitHostSerial), DisableParallelization = true)]
public sealed class IdeCockpitHostSerial;

[Collection(nameof(IdeCockpitHostSerial))]
public class IdeCockpitHostChannelTests
{
    public IdeCockpitHostChannelTests()
    {
        IdeCockpitHostChannel.Configure(new CockpitHostSettings());
        _ = IdeCockpitHostChannel.Handle(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement("stop")
        });
    }

    [Fact]
    public void Scene_defaults_agent_only_down()
    {
        using var doc = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson());
        Assert.Equal("down", doc.RootElement.GetProperty("gui_host").GetString());
        Assert.Equal("agent-only", doc.RootElement.GetProperty("host_profile").GetString());
        Assert.Equal("none", doc.RootElement.GetProperty("config_source").GetString());
    }

    [Fact]
    public void Start_without_exe_fails_clearly()
    {
        var prev = Environment.GetEnvironmentVariable(IdeCockpitHostChannel.EnvExe);
        try
        {
            Environment.SetEnvironmentVariable(IdeCockpitHostChannel.EnvExe, null);
            IdeCockpitHostChannel.Configure(new CockpitHostSettings());
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
    public void Start_uses_toml_configured_exe()
    {
        if (!TryResolveStandIn(out var exe, out var standInArgs))
            return;

        var prev = Environment.GetEnvironmentVariable(IdeCockpitHostChannel.EnvExe);
        var pid = 0;
        try
        {
            Environment.SetEnvironmentVariable(IdeCockpitHostChannel.EnvExe, null);
            IdeCockpitHostChannel.Configure(new CockpitHostSettings { Exe = exe });

            using (var started = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(
                       new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                       {
                           ["op"] = JsonSerializer.SerializeToElement("start"),
                           ["args"] = JsonSerializer.SerializeToElement(standInArgs)
                       })))
            {
                Assert.True(started.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("up", started.RootElement.GetProperty("gui_host").GetString());
                pid = started.RootElement.GetProperty("pid").GetInt32();
            }

            StopAndEnsureDead(pid);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdeCockpitHostChannel.EnvExe, prev);
            IdeCockpitHostChannel.Configure(new CockpitHostSettings());
            ForceKill(pid);
        }
    }

    [Fact]
    public void Start_and_stop_lifecycle_with_headless_stand_in()
    {
        if (!TryResolveStandIn(out var exe, out var standInArgs))
            return; // non-Windows / no stand-in

        var pid = 0;
        try
        {
            var argsStart = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("start"),
                ["path"] = JsonSerializer.SerializeToElement(exe),
                ["args"] = JsonSerializer.SerializeToElement(standInArgs)
            };
            using (var started = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(argsStart)))
            {
                Assert.True(started.RootElement.GetProperty("ok").GetBoolean());
                Assert.Equal("up", started.RootElement.GetProperty("gui_host").GetString());
                pid = started.RootElement.GetProperty("pid").GetInt32();
                Assert.True(pid > 0);
            }

            StopAndEnsureDead(pid);
        }
        finally
        {
            ForceKill(pid);
        }
    }

    static void StopAndEnsureDead(int pid)
    {
        using var stopped = JsonDocument.Parse(IdeCockpitHostChannel.HandleJson(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["op"] = JsonSerializer.SerializeToElement("stop")
            }));
        Assert.True(stopped.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("down", stopped.RootElement.GetProperty("gui_host").GetString());
        Assert.Equal("agent-only", stopped.RootElement.GetProperty("host_profile").GetString());
        ForceKill(pid);
        Assert.False(IsAlive(pid), $"stand-in pid={pid} still alive after stop");
    }

    /// <summary>Hidden pwsh sleep — same PID as Start (no Win11 Notepad Store remap).</summary>
    static bool TryResolveStandIn(out string exe, out string args)
    {
        args = "-NoProfile -WindowStyle Hidden -Command \"Start-Sleep -Seconds 120\"";
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh))
        {
            exe = pwsh;
            return true;
        }

        // Fallback: System32 ping stays under the started PID (unlike notepad.exe alias).
        var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        if (!File.Exists(ping))
        {
            exe = "";
            args = "";
            return false;
        }

        exe = ping;
        args = "-t 127.0.0.1";
        return true;
    }

    static void ForceKill(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited)
                return;
            p.Kill(entireProcessTree: true);
            _ = p.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            /* already gone */
        }
        catch (InvalidOperationException)
        {
            /* already gone */
        }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
