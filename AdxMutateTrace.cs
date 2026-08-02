#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

/// <summary>
/// In-proc ring of recent mutates for <c>go=quality scope=assert</c> (ADX-HX-001).
/// Harness path via <see cref="Record"/>; host Write / outside-IDE drift via <see cref="RecordOutsideIde"/>.
/// </summary>
internal static class AdxMutateTrace
{
    public const string OpHostWrite = "host_write";

    const int Cap = 64;
    static readonly ConcurrentQueue<Entry> Q = new();
    static readonly ConcurrentDictionary<string, string> OutsideEpisode = new(StringComparer.OrdinalIgnoreCase);

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
        Enqueue(new Entry(DateTimeOffset.UtcNow, path, op, isCreate, pathExistedBefore, ok));
    }

    /// <summary>
    /// Material disk drift on an open buffer — Cursor host Write / external editor.
    /// Deduped per path+reason+mtime episode (cleared on AcknowledgeDisk).
    /// </summary>
    public static void RecordOutsideIde(string path, string? reason, DateTime? diskMtimeUtc = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var key = path.Trim();
        var stamp = $"{reason ?? "outside"}|{diskMtimeUtc?.Ticks ?? 0}";
        if (OutsideEpisode.TryGetValue(key, out var prev) && prev == stamp)
            return;

        OutsideEpisode[key] = stamp;
        Enqueue(new Entry(
            DateTimeOffset.UtcNow,
            key,
            OpHostWrite,
            IsCreate: false,
            PathExistedBefore: true,
            GuidelineOk: false));
    }

    /// <summary>Allow a fresh host_write mark after reload / Don't Reload ack.</summary>
    public static void ClearOutsideIdeMark(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        OutsideEpisode.TryRemove(path.Trim(), out _);
    }

    public static IReadOnlyList<Entry> Snapshot() => Q.ToArray();

    public static object EvaluateRecent()
    {
        var all = Snapshot();
        var bad = all.Where(e => !e.GuidelineOk).Take(12).ToArray();
        var host = bad.Count(e => string.Equals(e.Op, OpHostWrite, StringComparison.OrdinalIgnoreCase));
        return new
        {
            id = "ADX-HX-001.trace",
            ok = bad.Length == 0,
            severity = bad.Length == 0 ? "ok" : "warn",
            sampled = all.Count,
            violations = bad.Length,
            host_write = host,
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
                : host > 0
                    ? $"habitat_trace WARN×{bad.Length} host_write×{host}"
                    : $"habitat_trace WARN×{bad.Length} set_text_on_existing"
        };
    }

    static void Enqueue(Entry entry)
    {
        Q.Enqueue(entry);
        while (Q.Count > Cap && Q.TryDequeue(out _))
        {
            /* trim */
        }
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
