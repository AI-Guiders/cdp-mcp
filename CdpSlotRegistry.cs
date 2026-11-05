#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CdpMcp;

/// <summary>
/// ADR-0209 slot registry — pure in-memory WebSocket-backed registry in the Gatekeeper tower.
/// Service slots maintain a persistent WebSocket connection to <c>ws://127.0.0.1:8771/_internal/slot/ws</c>,
/// sending <c>{pid, port, sha, build_utc}</c> on connect and every 5s.
/// Disconnect immediately removes the slot; no database or pulse files touch the disk.
/// </summary>
public static class CdpSlotRegistry
{
    public const string WsPath = "/_internal/slot/ws";
    public const string ListPath = "/_internal/slot/list";
    public const int FirstSlotPort = 8772;
    public const int LastSlotPort = 8871;

    /// <summary>Rows older than this without a WebSocket frame are suspected dead; healthz probe is the final arbiter.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(15);

    static readonly ConcurrentDictionary<int, CdpSlotRecord> Slots = new();

    static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;
        if (pid == Environment.ProcessId)
            return true;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            if (proc.HasExited)
                return false;
            var name = proc.ProcessName;
            return name.Equals("CdpService", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CdpMcp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when a loopback bind on the port succeeds right now.</summary>
    public static bool IsPortFree(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            try { listener.Stop(); }
            catch { /* best effort */ }
        }
    }

    /// <summary>First free TCP port in the slot range (8772..8871). The gatekeeper's 8771 is never a slot.</summary>
    public static int PickFreePort()
    {
        for (var port = FirstSlotPort; port <= LastSlotPort; port++)
        {
            if (IsPortFree(port))
                return port;
        }

        throw new IOException($"No free slot port in {FirstSlotPort}..{LastSlotPort}.");
    }

    public static bool Upsert(CdpSlotRecord record) => Upsert( null, record);

    public static bool Upsert(string? stateRoot, CdpSlotRecord record)
    {
        if (record is null || record.Pid <= 0 || record.Port is <= 0 or > 65535)
            return false;

        var copy = new CdpSlotRecord
        {
            Pid = record.Pid,
            Port = record.Port,
            Sha = record.Sha ?? "",
            BuildUtc = record.BuildUtc ?? "",
            LastSeenUtc = record.LastSeenUtc == default ? DateTimeOffset.UtcNow : record.LastSeenUtc
        };
        Slots[copy.Pid] = copy;
        return true;
    }

    public static bool Touch(int pid)
    {
        if (pid <= 0 || !Slots.TryGetValue(pid, out var existing))
            return false;

        existing.LastSeenUtc = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Fresh rows (within MaxAge and process alive), newest build first (deterministic).</summary>
    public static IReadOnlyList<CdpSlotRecord> Fresh(string? stateRoot = null)
    {
        var cutoff = DateTimeOffset.UtcNow - MaxAge;
        var result = new List<CdpSlotRecord>();

        foreach (var kv in Slots)
        {
            var row = kv.Value;
            if (row.LastSeenUtc < cutoff || !IsProcessAlive(row.Pid))
            {
                Slots.TryRemove(kv.Key, out _);
                continue;
            }

            result.Add(row);
        }

        return result
            .OrderByDescending(x => x.BuildUtc ?? "", StringComparer.Ordinal)
            .ThenByDescending(x => x.Pid)
            .ToList();
    }

    public static bool Remove(int pid) => Remove(null, pid);

    public static bool Remove(string? stateRoot, int pid)
    {
        if (pid <= 0)
            return false;

        return Slots.TryRemove(pid, out _);
    }

    public static void ResetForTests() => Slots.Clear();
}

public sealed class CdpSlotRecord
{
    public int Pid { get; set; }
    public int Port { get; set; }
    public string? Sha { get; set; }
    public string? BuildUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
