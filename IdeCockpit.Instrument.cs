#nullable enable

namespace CdpMcp;

/// <summary>
/// Instrument peel — desk seat mounts as instrument deck (CIDE ADR 0047/0063 spirit, no Avalonia).
/// Seats P|F|M are semantic anchors; organs are ordered instrument ids on the desk surface.
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

    /// <summary>Describe current seats mode as an instrument deck for surface mounts.</summary>
    public static DeskInstrumentDeck DescribeSeatsInstrumentDeck()
    {
        if (!IdeDeskSeats.IsSeatsMode())
        {
            return new DeskInstrumentDeck(
                "desk-tiles",
                "tiles",
                "tiles",
                Array.Empty<DeskInstrument>());
        }

        var snap = IdeDeskSeats.Snapshot();
        var instruments = new List<DeskInstrument>(IdeDeskSeats.Order.Length);
        foreach (var seat in IdeDeskSeats.Order)
        {
            if (!snap.TryGetValue(seat, out var organ) || string.IsNullOrWhiteSpace(organ))
                continue;
            instruments.Add(new DeskInstrument(
                Id: seat + ":" + organ,
                Seat: seat,
                Organ: organ!,
                Anchor: "seat/" + seat));
        }

        return new DeskInstrumentDeck(
            DeckId: "desk-seats",
            SemanticAnchorId: "seats",
            LayoutPattern: "seats-row",
            Instruments: instruments);
    }

    /// <summary>Compact pulse for arch board / SA — instrument mount count.</summary>
    public static object InstrumentPulse()
    {
        var deck = DescribeSeatsInstrumentDeck();
        return new
        {
            seam = "instrument",
            adr = "0063",
            peel = true,
            deck_id = deck.DeckId,
            anchor = deck.SemanticAnchorId,
            layout = deck.LayoutPattern,
            count = deck.Instruments.Count,
            instruments = deck.Instruments.Select(i => new { id = i.Id, seat = i.Seat, organ = i.Organ })
        };
    }
}
