#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;
internal static partial class IdeFlightDataRecorder
{
    public static IReadOnlyList<FdrEvent> ReadTail(int limit = 40)
    {
        limit = Math.Clamp(limit, 1, 500);
        lock (Gate)
        {
            var path = TapePath;
            if (!File.Exists(path))
                return[];
            var lines = File.ReadAllLines(path);
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
        lock (Gate)
        {
            try
            {
                var path = TapePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                var line = JsonSerializer.Serialize(ev, JsonOpts);
                File.AppendAllText(path, line + "\n", Encoding.UTF8);
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
            var lines = File.ReadAllLines(path);
            if (lines.Length <= maxLines)
                return;
            var keep = lines.AsSpan(lines.Length - maxLines).ToArray();
            File.WriteAllLines(path, keep, Encoding.UTF8);
        }
        catch
        {
        /* best-effort */
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