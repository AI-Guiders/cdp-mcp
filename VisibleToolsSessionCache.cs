#nullable enable
using Cdp.Core;
using ModelContextProtocol.Protocol;
using Tool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>
/// Service-side visible-tools amortisation (L4): cache ListTools compose by
/// (capabilitiesRev, phase, object, language). Invalidate via Bump on rev.
/// </summary>
internal static class VisibleToolsSessionCache
{
    static readonly object Gate = new();
    static string? _key;
    static List<Tool>? _tools;
    static int _hits;
    static int _misses;

    public static string Fingerprint(long rev, SessionContext session) =>
        rev + "|" +
        (session.Phase.ToString() ?? "") + "|" +
        (session.Object.ToString() ?? "") + "|" +
        (session.Language ?? "") + "|" +
        (session.Intent?.ToString() ?? "");

    public static bool TryGet(string fingerprint, out List<Tool>? tools)
    {
        lock (Gate)
        {
            if (_tools is not null && string.Equals(_key, fingerprint, StringComparison.Ordinal))
            {
                _hits++;
                tools = Clone(_tools);
                return true;
            }

            tools = null;
            return false;
        }
    }

    public static void Put(string fingerprint, List<Tool> tools)
    {
        lock (Gate)
        {
            _key = fingerprint;
            _tools = Clone(tools);
            _misses++;
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _key = null;
            _tools = null;
        }
    }

    public static (int Hits, int Misses, bool Warm) Stats()
    {
        lock (Gate)
            return (_hits, _misses, _tools is not null);
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            _key = null;
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
