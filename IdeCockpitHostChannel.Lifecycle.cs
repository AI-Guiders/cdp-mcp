#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cdp.Core;

namespace CdpMcp;
internal static partial class IdeCockpitHostChannel
{
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
            var error = (string? )null;
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

    static bool PathsEqual(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
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
}