#nullable enable
using CdpMcp.Cockpit.Instrument;

namespace CdpMcp;

/// <summary>
/// Instrument peel — desk seat mounts via <see cref="DeskInstrumentMountRegistry"/> (ADR 0063).
/// </summary>
internal static partial class IdeCockpit
{
    public readonly record struct DeskInstrument(
        string Id,
        string Seat,
        string Organ,
        string Anchor);

    public readonly record struct DeskInstrumentDeck(
        string DeckId,
        string SemanticAnchorId,
        string LayoutPattern,
        IReadOnlyList<DeskInstrument> Instruments);

    /// <summary>Sync seats → registry and describe instrument deck.</summary>
    public static DeskInstrumentDeck DescribeSeatsInstrumentDeck()
    {
        var reg = DeskInstrumentHost.Current;
        if (!IdeDeskSeats.IsSeatsMode())
        {
            reg.SyncFromSeats(new Dictionary<string, string?>(), Array.Empty<string>());
            var empty = reg.Describe("desk-tiles", "tiles", "tiles");
            return ToDesk(empty);
        }

        reg.SyncFromSeats(IdeDeskSeats.Snapshot(), IdeDeskSeats.Order);
        return ToDesk(reg.Describe());
    }

    static DeskInstrumentDeck ToDesk(InstrumentDeckDescriptor d)
    {
        var instruments = d.OrderedInstrumentIds.Select(id =>
        {
            var parts = id.Split(':', 2);
            var seat = parts.Length > 0 ? parts[0] : "";
            var organ = parts.Length > 1 ? parts[1] : "";
            return new DeskInstrument(id, seat, organ, "seat/" + seat);
        }).ToArray();
        return new DeskInstrumentDeck(d.DeckId, d.SemanticAnchorId, d.LayoutPattern, instruments);
    }

    public static object InstrumentPulse()
    {
        var deck = DescribeSeatsInstrumentDeck();
        return new
        {
            seam = "instrument",
            adr = "0063",
            peel = true,
            real = true,
            deck_id = deck.DeckId,
            anchor = deck.SemanticAnchorId,
            layout = deck.LayoutPattern,
            count = deck.Instruments.Count,
            instruments = deck.Instruments.Select(i => new { id = i.Id, seat = i.Seat, organ = i.Organ })
        };
    }
}
