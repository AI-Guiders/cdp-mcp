#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>
/// In-proc ring of recent buffer mutates for <c>go=quality scope=assert</c> (ADX-HX-001).
/// Host Write still bypasses — this only sees harness path.
/// </summary>
internal static class AdxMutateTrace
{
    const int Cap = 64;
    static readonly ConcurrentQueue<Entry> Q = new();

    public sealed record Entry(
        DateTimeOffset AtUtc,
        string Path,
        string Op,
        bool IsCreate,
        bool PathExistedBefore,
        bool GuidelineOk);

    public static void Record(string path, string op, bool isCreate, bool pathExistedBefore)
    {
        var ok = AdxHabitatMutateKernel.GuidelineOk(isCreate, pathExistedBefore, op);
        Q.Enqueue(new Entry(DateTimeOffset.UtcNow, path, op, isCreate, pathExistedBefore, ok));
        while (Q.Count > Cap && Q.TryDequeue(out _))
        {
            /* trim */
        }
    }

    public static IReadOnlyList<Entry> Snapshot()
    {
        return Q.ToArray();
    }

    public static object EvaluateRecent()
    {
        var all = Snapshot();
        var bad = all.Where(e => !e.GuidelineOk).Take(12).ToArray();
        return new
        {
            id = "ADX-HX-001.trace",
            ok = bad.Length == 0,
            severity = bad.Length == 0 ? "ok" : "warn",
            sampled = all.Count,
            violations = bad.Length,
            recent_bad = bad.Select(e => new
            {
                at = e.AtUtc.ToString("o"),
                path = Short(e.Path),
                op = e.Op,
                path_existed_before = e.PathExistedBefore,
                is_create = e.IsCreate
            }).ToArray(),
            pulse = bad.Length == 0
                ? $"habitat_trace ok ×{all.Count}"
                : $"habitat_trace WARN×{bad.Length} set_text_on_existing"
        };
    }

    static string Short(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch
        {
            return path;
        }
    }
}
