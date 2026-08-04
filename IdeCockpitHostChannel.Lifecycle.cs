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

    /// <summary>path= → toml exe (mtime refresh) → env escape. Caller holds Gate.</summary>
    static string? ResolveExe(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return Path.GetFullPath(overridePath.Trim());
        RefreshCfgFromTomlIfNeeded();
        if (!string.IsNullOrWhiteSpace(_cfg.Exe))
            return Path.GetFullPath(_cfg.Exe.Trim());
        var env = Environment.GetEnvironmentVariable(EnvExe);
        return string.IsNullOrWhiteSpace(env) ? null : Path.GetFullPath(env.Trim());
    }

    /// <summary>Pick up install-toml edits without MCP remount. Caller holds Gate.</summary>
    static void RefreshCfgFromTomlIfNeeded()
    {
        if (_configPath is null || !File.Exists(_configPath))
            return;
        DateTime mtime;
        try
        {
            mtime = File.GetLastWriteTimeUtc(_configPath);
        }
        catch
        {
            return;
        }

        if (_configMtimeUtc is { } prev && mtime == prev)
            return;

        try
        {
            _cfg = CdpSettings.Load(_configPath).CockpitHost;
            _configMtimeUtc = mtime;
        }
        catch
        {
            /* keep previous cfg */
        }
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

    /// <summary>
    /// Same process name, different path (Debug vs Release Glass).
    /// Cabin-family only — never adopt generic shells (pwsh stand-in tests).
    /// </summary>
    static List<(int Pid, string Exe)> ListCabinPathOrphans(string preferredExe)
    {
        var list = new List<(int, string)>();
        if (!IsCabinFamilyExe(preferredExe))
            return list;
        var name = Path.GetFileNameWithoutExtension(preferredExe);
        if (string.IsNullOrWhiteSpace(name))
            return list;
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

                    if (path is null || !IsCabinFamilyExe(path))
                        continue;
                    if (PathsEqual(path, preferredExe))
                        continue;
                    list.Add((p.Id, Path.GetFullPath(path)));
                }
            }
            catch
            {
                /* skip */
            }
        }

        return list;
    }

    static bool IsCabinFamilyExe(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Contains("GlassCockpit", StringComparison.OrdinalIgnoreCase);

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