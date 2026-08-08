#nullable enable
using Cdp.IntercomJournal;

namespace CdpMcp;

/// <summary>
/// Virtual History — durable Radio journal in <c>intercom.witdb</c> beside last-wins LATEST.
/// Human Glass loads tail; PF queries via <c>cdp_intercom op=history</c>.
/// </summary>
internal static partial class CideIntercomVoiceLatch
{
    public static string JournalPath => IntercomJournalStore.DbPath(StateRoot);

    public static string LegacyJournalJsonlPath => IntercomJournalStore.LegacyJsonlPath(StateRoot);

    /// <summary>Append voice doc to WitDB journal (dedupe by id). Returns false if not durable.</summary>
    public static bool AppendJournal(IntercomVoiceDoc doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.Body))
            return false;

        var row = new IntercomJournalRow
        {
            Id = doc.Id,
            FromSeat = doc.FromSeat,
            ToSeat = doc.ToSeat,
            Body = doc.Body,
            Origin = doc.Origin,
            Name = doc.Name,
            Kind = doc.Kind,
            Channel = doc.Channel,
            StampedUtc = doc.StampedUtc,
            Acked = doc.Acked
        };

        for (var i = 0; i < 3; i++)
        {
            if (IntercomJournalStore.TryAppend(StateRoot, row))
                return true;
            Thread.Sleep(40 * (i + 1));
        }

        return false;
    }

    /// <summary>Last N journal entries (oldest→newest within the window).</summary>
    public static IReadOnlyList<IntercomVoiceDoc> LoadJournalTail(int limit = 40)
    {
        var rows = IntercomJournalStore.LoadTail(StateRoot, limit);
        return rows.Select(ToVoiceDoc).ToList();
    }

    /// <summary>Journal search by body/name contains. Empty query → tail.</summary>
    public static IReadOnlyList<IntercomVoiceDoc> SearchJournal(string? query, int limit = 40)
    {
        var rows = IntercomJournalStore.SearchContains(StateRoot, query, limit);
        return rows.Select(ToVoiceDoc).ToList();
    }

    public static int JournalCount() => IntercomJournalStore.Count(StateRoot);

    static IntercomVoiceDoc ToVoiceDoc(IntercomJournalRow row) => new()
    {
        Schema = Schema,
        Id = row.Id,
        FromSeat = row.FromSeat,
        ToSeat = row.ToSeat,
        Body = row.Body,
        Origin = row.Origin,
        Name = row.Name,
        Kind = row.Kind,
        Channel = row.Channel,
        StampedUtc = row.StampedUtc,
        Acked = row.Acked
    };
}
