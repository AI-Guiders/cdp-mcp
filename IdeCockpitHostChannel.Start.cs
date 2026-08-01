#nullable enable
using System.Diagnostics;
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Start / spawn cluster for cockpit host (ADR-0019).</summary>
internal static partial class IdeCockpitHostChannel
{
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
}
