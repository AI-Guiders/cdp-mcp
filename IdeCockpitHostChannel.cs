#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;

/// <summary>
/// Anchor Start/Stop — operator GUI cockpit host (ADR-0019 companion).
/// Meta <c>cdp_cockpit_host</c> / <c>go=cockpit_start|cockpit_stop</c>.
/// Config SSOT: <c>[cockpit_host] exe</c> in cdp-mcp.toml (process layer).
/// Start <c>path=</c> overrides once; env <c>CDP_COCKPIT_HOST_EXE</c> is escape only.
/// Runtime latch: in-proc + OS rediscover by exe path (no sidecar JSON).
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
    static CockpitHostSettings _cfg = new();
    static HostState? _live;

    /// <summary>Legacy stub path — deleted on Configure so remounts do not revive JSON latch.</summary>
    public static string LegacyStatePath => Path.Combine(CdpProfile.StateRoot, "cockpit-host.json");

    public static void Configure(CockpitHostSettings settings)
    {
        _cfg = settings ?? new CockpitHostSettings();
        TryDeleteLegacyJson();
    }

    public static string HandleJson(IReadOnlyDictionary<string, JsonElement>? args = null) =>
        JsonSerializer.Serialize(Handle(args), JsonOpts);

    /// <summary>Shared pulse for ICM / host scenes (no JSON round-trip).</summary>
    public static CockpitHostProfile.Snapshot GetHostPulse()
    {
        var st = Snapshot();
        return st is null
            ? new CockpitHostProfile.Snapshot("down", "agent-only", null)
            : new CockpitHostProfile.Snapshot("up", "dual-cockpit", st.Pid);
    }

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
        var st = Snapshot();
        return new
        {
            ok = true,
            schema = SchemaVersion,
            tool = ToolName,
            op = "scene",
            pulse = st is not null
                ? $"cockpit_host · up · pid={st.Pid}"
                : "cockpit_host · down · agent-only",
            gui_host = st is not null ? "up" : "down",
            host_profile = st is not null ? "dual-cockpit" : "agent-only",
            pid = st?.Pid,
            exe = st?.Exe,
            started_utc = st?.StartedUtc,
            exe_configured = ResolveExe(null) is not null,
            config_source = ConfigSourceLabel(),
            env_escape = EnvExe,
            hint = st is not null
                ? "op=stop to close GUI; MCP/ICM keep running."
                : "op=start path=… or [cockpit_host] exe in cdp-mcp.toml (env CDP_COCKPIT_HOST_EXE = escape). Melody/settings load with shell — do not strip them."
        };
    }

    static object Start(IReadOnlyDictionary<string, JsonElement> args)
    {
        lock (Gate)
        {
            var existing = SnapshotLocked();
            if (existing is not null)
            {
                var latchesAlready = CockpitHostLatchHydration.TouchAgentLatchesForHostStart();
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
                    latches_hydrated = latchesAlready,
                    pulse = $"cockpit_host · already up · pid={existing.Pid} · latches={latchesAlready}",
                    hint = "Host already running; latches re-stamped for glass."
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
                    config_source = ConfigSourceLabel(),
                    env_escape = EnvExe,
                    hint = "Set [cockpit_host] exe in cdp-mcp.toml (remount), or pass path=. Env CDP_COCKPIT_HOST_EXE is escape only. Does not launch Avalonia by guessing."
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

            var argsLine = Opt(args, "args");
            if (ContainsMcpStdioGuard(argsLine))
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = "args must not include --mcp-stdio",
                    hint = "Cockpit host is the GUI shell (Melody + settings.toml). MCP stdio stays on the agent process."
                };
            }

            var workDir = ResolveWorkingDirectory();
            if (!TrySpawnHost(exe, workDir, argsLine, out var pid, out var spawnError))
            {
                return new
                {
                    ok = false,
                    schema = SchemaVersion,
                    op = "start",
                    error = spawnError,
                    exe
                };
            }

            _live = new HostState
            {
                Pid = pid,
                Exe = exe,
                StartedUtc = DateTimeOffset.UtcNow.ToString("O")
            };
            var latches = CockpitHostLatchHydration.TouchAgentLatchesForHostStart();
            return new
            {
                ok = true,
                schema = SchemaVersion,
                op = "start",
                gui_host = "up",
                host_profile = "dual-cockpit",
                pid = _live.Pid,
                exe = _live.Exe,
                started_utc = _live.StartedUtc,
                working_directory = workDir,
                latches_hydrated = latches,
                pulse = $"cockpit_host · started · pid={_live.Pid} · latches={latches}",
                hint = "GUI up with Melody/settings. Prefer cdp_icm / cdp_land; Stop via op=stop. Latches re-stamped for glass projectors."
            };
        }
    }

    /// <summary>Optional session ProjectRoot; falls back to exe directory.</summary>
    internal static Func<string?>? ProjectRootResolver { get; set; }

    static bool TrySpawnHost(string exe, string workDir, string? argsLine, out int pid, out string error)
    {
        pid = 0;
        error = "";
        ProcessStartInfo psi = new()
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = workDir
        };
        if (!string.IsNullOrWhiteSpace(argsLine))
            psi.Arguments = argsLine;

        try
        {
            var proc = Process.Start(psi);
            if (proc is null)
            {
                error = "Process.Start returned null";
                return false;
            }

            pid = proc.Id;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string ResolveWorkingDirectory()
    {
        try
        {
            var root = ProjectRootResolver?.Invoke();
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return root!;
        }
        catch
        {
            /* fall through */
        }

        var configured = ResolveExe(null);
        return Path.GetDirectoryName(configured) ?? Environment.CurrentDirectory;
    }

    static bool ContainsMcpStdioGuard(string? argsLine)
    {
        if (string.IsNullOrWhiteSpace(argsLine))
            return false;
        return argsLine.Contains("--mcp-stdio", StringComparison.OrdinalIgnoreCase);
    }

    static object Stop()
    {
        lock (Gate)
        {
            var st = SnapshotLocked();
            if (st is null)
            {
                _live = null;
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

            _live = null;
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

    /// <summary>path= → toml exe → env escape.</summary>
    static string? ResolveExe(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());
        if (!string.IsNullOrWhiteSpace(_cfg.Exe))
            return Path.GetFullPath(_cfg.Exe.Trim());
        var env = Environment.GetEnvironmentVariable(EnvExe);
        return string.IsNullOrWhiteSpace(env) ? null : Path.GetFullPath(env.Trim());
    }

    static string ConfigSourceLabel()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.Exe))
            return "toml";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvExe)))
            return "env_escape";
        return "none";
    }

    static HostState? Snapshot()
    {
        lock (Gate)
            return SnapshotLocked();
    }

    static HostState? SnapshotLocked()
    {
        if (_live is { Pid: > 0 } && IsAlive(_live.Pid))
            return _live;

        _live = null;
        var preferred = ResolveExe(null);
        if (preferred is null)
            return null;

        var found = FindByExePath(preferred);
        if (found is not null)
            _live = found;
        return _live;
    }

    static HostState? FindByExePath(string preferredExe)
    {
        var name = Path.GetFileNameWithoutExtension(preferredExe);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                using (p)
                {
                    if (p.HasExited)
                        continue;
                    string? path = null;
                    try
                    {
                        path = p.MainModule?.FileName;
                    }
                    catch
                    {
                        /* access denied — skip */
                    }

                    if (path is null)
                        continue;
                    if (!PathsEqual(path, preferredExe))
                        continue;
                    return new HostState
                    {
                        Pid = p.Id,
                        Exe = preferredExe,
                        StartedUtc = null
                    };
                }
            }
            catch
            {
                /* skip */
            }
        }

        return null;
    }

    static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

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

    static void TryDeleteLegacyJson()
    {
        try
        {
            if (File.Exists(LegacyStatePath))
                File.Delete(LegacyStatePath);
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
