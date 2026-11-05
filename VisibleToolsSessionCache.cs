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
    static readonly Dictionary<string, List<Tool>> ByFingerprint = new(StringComparer.Ordinal);
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
            if (ByFingerprint.TryGetValue(fingerprint, out var cached))
            {
                _hits++;
                tools = Clone(cached);
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
            ByFingerprint[fingerprint] = Clone(tools);
            _misses++;
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            ByFingerprint.Clear();
        }
    }

    public static (int Hits, int Misses, bool Warm) Stats()
    {
        lock (Gate)
            return (_hits, _misses, ByFingerprint.Count > 0);
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            ByFingerprint.Clear();
            _hits = 0;
            _misses = 0;
        }
    }

    static List<Tool> Clone(List<Tool> src) =>
        src.Select(t => new Tool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema,
            // OutputSchema must survive cache hits — a clone without it made
            // every post-warmup tools/list serve untyped tools, so code-mode
            // clients fell back to Promise<{}> even for declared contracts.
            OutputSchema = t.OutputSchema
        }).ToList();
}
