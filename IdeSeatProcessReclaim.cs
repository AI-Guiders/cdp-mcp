#nullable enable
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Cursor remount often leaves prior <c>CdpMcp.exe</c> on the same install path
/// (dogfood 16–20×). FileGate covers WitDB only — this reclaims the seat process.
/// Sibling seat (<c>D:\cdp-mcp-debug</c>) has a different exe path and is untouched.
/// Skip: env <c>CDP_SKIP_SEAT_RECLAIM=1</c> (tests / intentional multi).
/// </summary>
internal static class IdeSeatProcessReclaim
{
    public const string SkipEnv = "CDP_SKIP_SEAT_RECLAIM";

    static Mutex? _lifetime;
    static int _lastKilled;

    /// <summary>Pids killed on last <see cref="Ensure"/> (tests / ops pulse).</summary>
    public static int LastKilledCount => _lastKilled;

    public static void Ensure()
    {
        _lastKilled = 0;
        if (IsSkipEnabled())
            return;

        string? selfExe;
        try
        {
            selfExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            selfExe = null;
        }

        if (string.IsNullOrWhiteSpace(selfExe))
            return;

        var selfId = Environment.ProcessId;
        var victims = CollectOtherSameExePids(selfExe, selfId);
        foreach (var pid in victims)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited)
                    continue;
                p.Kill(entireProcessTree: true);
                _lastKilled++;
            }
            catch
            {
                /* already gone / access */
            }
        }

        if (_lastKilled > 0)
        {
            try { Thread.Sleep(150); }
            catch { /* ignore */ }
        }

        _lifetime ??= TakeLifetimeMutex(selfExe);
    }

    internal static bool IsSkipEnabled()
    {
        var v = Environment.GetEnvironmentVariable(SkipEnv);
        return v is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    internal static bool PathsEqual(string a, string b) =>
        string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim());

    /// <summary>Enumerate other live PIDs whose MainModule matches <paramref name="selfExe"/>.</summary>
    internal static List<int> CollectOtherSameExePids(string selfExe, int selfId)
    {
        var name = Path.GetFileNameWithoutExtension(selfExe);
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(name))
            return list;

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                using (p)
                {
                    if (p.Id == selfId || p.HasExited)
                        continue;
                    string? path = null;
                    try { path = p.MainModule?.FileName; }
                    catch { /* access denied */ }
                    if (path is null)
                        continue;
                    if (!PathsEqual(path, selfExe))
                        continue;
                    list.Add(p.Id);
                }
            }
            catch
            {
                /* skip */
            }
        }

        return list;
    }

    static Mutex TakeLifetimeMutex(string selfExe)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(NormalizePath(selfExe).ToLowerInvariant())))[..16];
        var name = $@"Local\CdpMcp.SeatExe.{hash}";
        var mutex = new Mutex(initiallyOwned: false, name: name);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(8)))
            {
                /* another same-exe still racing — keep handle; do not throw (stdio MCP must start) */
            }
        }
        catch (AbandonedMutexException)
        {
            /* prior owner died — we own it */
        }

        return mutex;
    }
}
