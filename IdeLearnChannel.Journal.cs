#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeLearnChannel
{
    static List<LearnEntry> LoadAll()
    {
        lock (Gate)
        {
            if (!File.Exists(JournalPath))
                return [];
            var list = new List<LearnEntry>();
            foreach (var line in File.ReadLines(JournalPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var e = JsonSerializer.Deserialize<LearnEntry>(line, JsonOpts);
                    if (e?.Id is { Length: > 0 })
                        list.Add(e);
                }
                catch
                {
                    // skip corrupt line
                }
            }
            return list;
        }
    }

    static int CountEntries()
    {
        lock (Gate)
        {
            if (!File.Exists(JournalPath))
                return 0;
            var n = 0;
            foreach (var line in File.ReadLines(JournalPath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    n++;
            }
            return n;
        }
    }

    static void Append(LearnEntry entry)
    {
        lock (Gate)
        {
            var dir = Path.GetDirectoryName(JournalPath)!;
            Directory.CreateDirectory(dir);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            File.AppendAllText(JournalPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    static void Upsert(LearnEntry entry)
    {
        lock (Gate)
        {
            var all = LoadAllUnlocked();
            for (var i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                    all[i] = entry;
            }

            var dir = Path.GetDirectoryName(JournalPath)!;
            Directory.CreateDirectory(dir);
            var tmp = JournalPath + ".tmp";
            using (var sw = new StreamWriter(tmp, false, Encoding.UTF8))
            {
                foreach (var e in all)
                    sw.WriteLine(JsonSerializer.Serialize(e, JsonOpts));
            }
            File.Move(tmp, JournalPath, overwrite: true);
        }
    }

    static List<LearnEntry> LoadAllUnlocked()
    {
        if (!File.Exists(JournalPath))
            return [];
        var list = new List<LearnEntry>();
        foreach (var line in File.ReadLines(JournalPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var e = JsonSerializer.Deserialize<LearnEntry>(line, JsonOpts);
                if (e?.Id is { Length: > 0 })
                    list.Add(e);
            }
            catch
            {
                // skip
            }
        }
        return list;
    }

    static string MakeId(DateTimeOffset now, string title)
    {
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var slug = Slug(title, 24);
        return $"l-{stamp}-{slug}";
    }

    static string Slug(string text, int max)
    {
        var s = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-");
        s = s.Trim('-');
        if (s.Length == 0)
            s = "card";
        if (s.Length > max)
            s = s[..max].Trim('-');
        return s;
    }

    static string SanitizeFile(string id) =>
        Regex.Replace(id, "[^a-zA-Z0-9._-]+", "-");

    static string FirstLine(string text, int max)
    {
        var line = text.Replace("\r", "").Split('\n')[0].Trim();
        if (line.Length == 0)
            return "learning";
        if (line.Length <= max)
            return line;
        return line[..(max - 1)].TrimEnd() + "…";
    }

    static List<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }
}

