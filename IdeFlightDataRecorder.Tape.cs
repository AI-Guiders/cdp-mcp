#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CdpMcp;

internal static partial class IdeFlightDataRecorder
{
    public static IReadOnlyList<FdrEvent> ReadTail(int limit = 40)
    {
        limit = Math.Clamp(limit, 1, 500);
        using var tapeGate = EnterTapeGate();
        lock (Gate)
        {
            var path = TapePath;
            TryMigrateLegacyTape(path);
            if (!File.Exists(path))
                return [];

            var lines = ReadAllLinesShared(path);
            var list = new List<FdrEvent>(Math.Min(limit, lines.Length));
            for (var i = lines.Length - 1; i >= 0 && list.Count < limit; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<FdrEvent>(line, JsonOpts);
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

    public static object Slim(FdrEvent e) => new
    {
        at = e.AtUtc,
        kind = e.Kind,
        tool = e.Tool,
        op = e.Op,
        go = e.Go,
        ms = e.ElapsedMs,
        outcome = e.Outcome,
        wake = e.WakeExceeded,
        phase = e.Phase,
        @object = e.Object,
        err = e.Error,
        call = e.CallId
    };

    static void Append(FdrEvent ev)
    {
        if (SuppressWriteForTests)
            return;

        using var tapeGate = EnterTapeGate();
        lock (Gate)
        {
            try
            {
                var path = TapePath;
                TryMigrateLegacyTape(path);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                var line = JsonSerializer.Serialize(ev, JsonOpts) + "\n";
                AppendLineShared(path, line);
                RotateIfNeeded(path, DefaultMaxLines);
            }
            catch
            {
                /* never break CallTool on FDR I/O */
            }
        }
    }

    static void RotateIfNeeded(string path, int maxLines)
    {
        try
        {
            var lines = ReadAllLinesShared(path);
            if (lines.Length <= maxLines)
                return;
            var keep = lines.AsSpan(lines.Length - maxLines).ToArray();
            WriteAllLinesShared(path, keep);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// Dual seats must not fight one FileShare.None tape — seat-local under StateRoot/{seat}/.
    /// Primary once inherits legacy workspace-root tape via Move (same pattern as WitDB).
    /// </summary>
    internal static void TryMigrateLegacyTape(string seatPath)
    {
        if (PathOverrideForTests is not null)
            return;
        if (File.Exists(seatPath))
            return;
        if (!File.Exists(LegacyTapePath))
            return;
        if (!string.Equals(IdeIgniteArmHost.Seat, "cdp", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var dir = Path.GetDirectoryName(seatPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Move(LegacyTapePath, seatPath);
        }
        catch
        {
            /* race / lock — next append creates seat file fresh */
        }
    }

    static void AppendLineShared(string path, string line)
    {
        using var fs = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        using var sw = new StreamWriter(fs, Encoding.UTF8);
        sw.Write(line);
        sw.Flush();
    }

    static string[] ReadAllLinesShared(string path)
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var list = new List<string>();
        while (sr.ReadLine() is { } line)
            list.Add(line);
        return list.ToArray();
    }

    static void WriteAllLinesShared(string path, string[] lines)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllLines(tmp, lines, Encoding.UTF8);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Cross-process gate for seat tape rotate/append (in-proc Gate alone is not enough).</summary>
    static IDisposable EnterTapeGate() => new FdrTapeGate(TapePath);

    sealed class FdrTapeGate : IDisposable
    {
        readonly Mutex _mutex;
        readonly bool _owned;

        public FdrTapeGate(string tapePath)
        {
            var key = string.IsNullOrWhiteSpace(tapePath)
                ? "default"
                : Path.GetFullPath(tapePath).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
            _mutex = new Mutex(initiallyOwned: false, name: $@"Local\CdpMcp.Fdr.{hash}");
            try
            {
                _owned = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
            }
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* not owner */ }
            }

            _mutex.Dispose();
        }
    }

    static int Percentile(int[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0)
            return 0;
        var idx = (int)Math.Clamp(Math.Ceiling(p * sortedAsc.Length) - 1, 0, sortedAsc.Length - 1);
        return sortedAsc[idx];
    }

    static string? OptArg(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => Truncate(el.GetString(), 64),
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length <= max ? s : s[..max];
    }
}
