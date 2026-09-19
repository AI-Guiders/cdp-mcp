#nullable enable
using ModelContextProtocol.Protocol;

namespace CdpMcpBridge;

/// <summary>
/// Bridge ListTools amortisation (L4): cache tools[] keyed by capabilitiesRev.
/// Invalidate when watcher sees a new rev (ADR-0202 list_changed still fires).
/// </summary>
internal static class CdpBridgeToolsCache
{
    static readonly object Gate = new();
    static long _rev = -1;
    static List<Tool>? _tools;
    static int _hits;
    static int _misses;

    public static bool TryGet(long rev, out List<Tool>? tools)
    {
        lock (Gate)
        {
            if (_tools is not null && _rev == rev)
            {
                _hits++;
                tools = Clone(_tools);
                return true;
            }

            tools = null;
            return false;
        }
    }

    public static void Put(long rev, List<Tool> tools)
    {
        lock (Gate)
        {
            _rev = rev;
            _tools = Clone(tools);
            _misses++;
        }
    }

    /// <summary>Watcher / list_changed — drop payload so next ListTools refetches.</summary>
    public static void Invalidate(long? newRev = null)
    {
        lock (Gate)
        {
            _tools = null;
            if (newRev is { } r)
                _rev = r;
            else
                _rev = -1;
        }
    }

    public static (int Hits, int Misses, long Rev, bool Warm) Stats()
    {
        lock (Gate)
            return (_hits, _misses, _rev, _tools is not null);
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            _rev = -1;
            _tools = null;
            _hits = 0;
            _misses = 0;
        }
    }

    static List<Tool> Clone(List<Tool> src) =>
        src.Select(t => new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();
}
