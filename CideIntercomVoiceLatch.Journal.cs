#nullable enable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// Virtual History — append-only intercom journal beside last-wins LATEST.
/// Human Glass loads tail; PF queries via <c>cdp_intercom op=history</c>.
/// </summary>
internal static partial class CideIntercomVoiceLatch
{
    static readonly object JournalGate = new();

    static readonly JsonSerializerOptions JournalJsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string JournalPath => Path.Combine(StateRoot, "intercom-journal.jsonl");

    /// <summary>Append voice doc to journal (dedupe by id). Best-effort.</summary>
    public static void AppendJournal(IntercomVoiceDoc doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.Body))
            return;

        lock (JournalGate)
        {
            try
            {
                Directory.CreateDirectory(StateRoot);
                if (File.Exists(JournalPath))
                {
                    foreach (var line in File.ReadLines(JournalPath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        try
                        {
                            var prev = JsonSerializer.Deserialize<IntercomVoiceDoc>(line, ReadOpts);
                            if (prev is not null
                                && string.Equals(prev.Id, doc.Id, StringComparison.OrdinalIgnoreCase))
                                return;
                        }
                        catch
                        {
                            /* skip corrupt */
                        }
                    }
                }

                var json = JsonSerializer.Serialize(doc, JournalJsonOpts);
                File.AppendAllText(JournalPath, json + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    /// <summary>Last N journal entries (oldest→newest within the window).</summary>
    public static IReadOnlyList<IntercomVoiceDoc> LoadJournalTail(int limit = 40)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        lock (JournalGate)
        {
            if (!File.Exists(JournalPath))
                return [];

            var all = new List<IntercomVoiceDoc>();
            foreach (var line in File.ReadLines(JournalPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var e = JsonSerializer.Deserialize<IntercomVoiceDoc>(line, ReadOpts);
                    if (e is not null && !string.IsNullOrWhiteSpace(e.Body))
                        all.Add(e);
                }
                catch
                {
                    /* skip */
                }
            }

            if (all.Count <= limit)
                return all;
            return all.GetRange(all.Count - limit, limit);
        }
    }

    public static int JournalCount()
    {
        lock (JournalGate)
        {
            if (!File.Exists(JournalPath))
                return 0;
            var n = 0;
            foreach (var line in File.ReadLines(JournalPath))
            {
                if (line.Length > 0)
                    n++;
            }

            return n;
        }
    }
}
