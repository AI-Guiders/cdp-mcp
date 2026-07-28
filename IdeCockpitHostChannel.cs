#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Anchor Start/Stop — operator GUI cockpit host (ADR-0019 companion).
/// Meta <c>cdp_cockpit_host</c> / <c>go=cockpit_start|cockpit_stop</c>.
/// Default agent-only; Start launches configured shell exe; Stop kills that pid only (not MCP).
/// Does not mutate Intent Melody / CascadeIdeSettings.
/// </summary>
internal static class IdeCockpitHostChannel
{
    public const string SchemaVersion = "cockpit_host/v1";
    public const string ToolName = "cdp_cockpit_host";
    public const string EnvExe = "CDP_COCKPIT_HOST_EXE";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly object Gate = new();

    public static string StatePath => Path.Combine(CdpProfile.StateRoot, "cockpit-host.json");

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(args), JsonOpts);

    public static object Handle(IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        return op switch
        {
            "start" or "up" or "open" => Start(args),
            "stop" or "down" or "close" => Stop(),
            _ => Scene()
        };
    }

    static object Scene()
    {
        var st = Load();
        var alive = st is { Pid: > 0 } && IsAlive(st.Pid);
        if (st is not null && st.Pid > 0 && !alive)
        {
            Clear();
            st = null;
        }

        return new
        {
            ok = true,
            schema = SchemaVersion,
            tool = ToolName,
            op = "scene",
            pulse = alive
                ? $"cockpit_host · up · pid={st!.Pid}"
                : "cockpit_host · down · agent-only",
            gui_host = alive ? "up" : "down",
            host_profile = alive ? "dual-cockpit" : "agent-only",
            pid = alive ? st!.Pid : (int?)null,
            exe = alive ? st!.Exe : null,
            started_utc = alive ? st!.StartedUtc : null,
            exe_configured = ResolveExe(null) is not null,
            env = EnvExe,
            hint = alive
                ? "op=stop to close GUI; MCP/ICM keep running."
                : $"op=start path=… or set {EnvExe}; Melody/settings load with shell — do not strip them."
        };
    }

    static object Start(IReadOnlyDictionary<string, JsonElement> args)
    {
        lock (Gate)
        {
            var existing = Load();
            if (existing is { Pid: > 0 } && IsAlive(existing.Pid))
            {
                return new
                {
                    ok = true,
                    schema = SchemaVersion,
                    op = "start",
                    already = true,
                    gui_host = "up",
                    host_profile = "dual-cockpit",
                    pid = existing.Pid,
                    exe = existing.Exe,
                    pulse = $"cockpit_host · already up · pid={existing.Pid}",
                    hint = "Host already running."
                };
            }

            var exe = ResolveExe(Opt(args, "path") ?? Opt(args, "exe"));
            if (exe is null)
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = "cockpit host exe not configured",
                    env = EnvExe,
                    hint = $"Set {EnvExe} to CascadeIDE (or thin shell) path, or pass path=. Does not launch Avalonia by guessing."
                };
            }

            if (!File.Exists(exe))
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = "exe not found",
                    exe,
                    hint = "Fix path / rebuild GUI shell."
                };
            }

            ProcessStartInfo psi = new()
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };
            var argsLine = Opt(args, "args");
            if (!string.IsNullOrWhiteSpace(argsLine))
                psi.Arguments = argsLine;

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = ex.Message,
                    exe
                };
            }

            if (proc is null)
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = "Process.Start returned null",
                    exe
                };
            }

            var doc = new HostState
            {
                Pid = proc.Id,
                Exe = exe,
                StartedUtc = DateTimeOffset.UtcNow.ToString("O")
            };
            Save(doc);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                op = "start",
                gui_host = "up",
                host_profile = "dual-cockpit",
                pid = doc.Pid,
                exe = doc.Exe,
                started_utc = doc.StartedUtc,
                pulse = $"cockpit_host · started · pid={doc.Pid}",
                hint = "GUI up. Prefer cdp_icm / cdp_land from shell; Stop via op=stop."
            };
        }
    }

    static object Stop()
    {
        lock (Gate)
        {
            var st = Load();
            if (st is null || st.Pid <= 0)
            {
                Clear();
                return new
                {
                    ok = true,
                    schema = SchemaVersion,
                    op = "stop",
                    gui_host = "down",
                    host_profile = "agent-only",
                    pulse = "cockpit_host · already down",
                    hint = "Nothing to stop."
                };
            }

            var killed = false;
            var error = (string?)null;
            if (IsAlive(st.Pid))
            {
                try
                {
                    using var p = Process.GetProcessById(st.Pid);
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000))
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(5000);
                    }

                    killed = true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    try
                    {
                        using var p = Process.GetProcessById(st.Pid);
                        p.Kill(entireProcessTree: true);
                        killed = true;
                    }
                    catch (Exception ex2)
                    {
                        error = ex2.Message;
                    }
                }
            }

            Clear();
            return new
            {
                ok = error is null,
                schema = SchemaVersion,
                op = "stop",
                gui_host = "down",
                host_profile = "agent-only",
                killed,
                was_pid = st.Pid,
                error,
                pulse = "cockpit_host · down · agent-only",
                hint = "MCP/ICM still running."
            };
        }
    }

    static string? ResolveExe(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());
        var env = Environment.GetEnvironmentVariable(EnvExe);
        return string.IsNullOrWhiteSpace(env) ? null : Path.GetFullPath(env.Trim());
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

    static HostState? Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            return JsonSerializer.Deserialize<HostState>(File.ReadAllText(StatePath), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    static void Save(HostState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts));
    }

    static void Clear()
    {
        try
        {
            if (File.Exists(StatePath))
                File.Delete(StatePath);
        }
        catch
        {
            /* ignore */
        }
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    sealed class HostState
    {
        public int Pid { get; set; }
        public string? Exe { get; set; }
        public string? StartedUtc { get; set; }
    }
}
