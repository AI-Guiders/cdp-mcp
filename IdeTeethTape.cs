#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Guest-host teeth tape — OOM tooth / CDT / remount·oom wake delivery.
/// Separate from FDR tool-call black box (ADR-0029).
/// </summary>
internal static class IdeTeethTape
{
    public const string Schema = "teeth_event/v1";
    public const int DefaultMaxLines = 1500;

    static readonly object Gate = new();
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string? PathOverrideForTests { get; set; }
    internal static bool SuppressWriteForTests { get; set; }

    static string? s_lastSubmitKind;
    static bool? s_lastCdtUp;
    static DateTimeOffset? s_lastCdtNoteUtc;

    public static string TapePath =>
        PathOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cdp-mcp",
            "teeth-tape.jsonl");

    public static string? LastSubmitKind
    {
        get { lock (Gate) return s_lastSubmitKind; }
    }

    public static bool? LastCdtUp
    {
        get { lock (Gate) return s_lastCdtUp; }
    }

    public static DateTimeOffset? LastCdtNoteUtc
    {
        get { lock (Gate) return s_lastCdtNoteUtc; }
    }

    /// <summary>Test hook — clear in-memory guest CDT/submit latch.</summary>
    internal static void ResetGuestForTests()
    {
        lock (Gate)
        {
            s_lastSubmitKind = null;
            s_lastCdtUp = null;
            s_lastCdtNoteUtc = null;
        }
    }

    public static void NoteGuest(string? submitKind, bool? cdtUp)
    {
        lock (Gate)
        {
            if (submitKind is { Length: > 0 })
                s_lastSubmitKind = submitKind.Trim().ToLowerInvariant();
            if (cdtUp is { } up)
                s_lastCdtUp = up;
            s_lastCdtNoteUtc = DateTimeOffset.UtcNow;
        }
    }

    public static void Record(
        string kind,
        string? detail = null,
        string? armId = null,
        string? reason = null,
        string? submitKind = null,
        long? downMs = null)
    {
        if (SuppressWriteForTests)
            return;

        if (submitKind is { Length: > 0 })
            NoteGuest(submitKind, cdtUp: null);

        Append(new TeethEvent
        {
            Kind = string.IsNullOrWhiteSpace(kind) ? "unknown" : kind.Trim(),
            ArmId = Truncate(armId, 80),
            Reason = Truncate(reason, 40),
            Detail = Truncate(detail, 240),
            SubmitKind = Truncate(submitKind, 24),
            DownMs = downMs,
            AtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
    }

    public static IReadOnlyList<TeethEvent> ReadTail(int limit = 40)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (Gate)
        {
            var path = TapePath;
            if (!File.Exists(path))
                return [];

            var lines = File.ReadAllLines(path);
            var list = new List<TeethEvent>(Math.Min(limit, lines.Length));
            for (var i = lines.Length - 1; i >= 0 && list.Count < limit; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<TeethEvent>(line, JsonOpts);
                    if (ev is not null)
                        list.Add(ev);
                }
                catch
                {
                    /* skip corrupt */
                }
            }

            list.Reverse();
            return list;
        }
    }

    public static object Slim(TeethEvent e) => new
    {
        at = e.AtUtc,
        kind = e.Kind,
        arm = e.ArmId,
        reason = e.Reason,
        submit = e.SubmitKind,
        down_ms = e.DownMs,
        detail = e.Detail
    };

    public sealed class TeethEvent
    {
        public string Schema { get; set; } = IdeTeethTape.Schema;
        public string Kind { get; set; } = "";
        public string? ArmId { get; set; }
        public string? Reason { get; set; }
        public string? Detail { get; set; }
        public string? SubmitKind { get; set; }
        public long? DownMs { get; set; }
        public string AtUtc { get; set; } = "";
    }

    static void Append(TeethEvent ev)
    {
        lock (Gate)
        {
            try
            {
                var path = TapePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(path, JsonSerializer.Serialize(ev, JsonOpts) + "\n", Encoding.UTF8);
                RotateIfNeeded(path, DefaultMaxLines);
            }
            catch
            {
                /* never break tooth/wake on I/O */
            }
        }
    }

    static void RotateIfNeeded(string path, int maxLines)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= maxLines)
                return;
            File.WriteAllLines(path, lines.AsSpan(lines.Length - maxLines).ToArray(), Encoding.UTF8);
        }
        catch
        {
            /* best-effort */
        }
    }

    static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length <= max ? s : s[..max];
    }
}
