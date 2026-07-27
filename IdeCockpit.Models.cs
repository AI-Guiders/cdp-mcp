#nullable enable
namespace CdpMcp;

/// <summary>SeatPane / Locus DTOs for desk surface.</summary>
internal static partial class IdeCockpit
{
    readonly record struct SeatPane(
        string Seat,
        string? Organ,
        bool Empty,
        bool Full,
        bool Ok,
        string Line,
        object? Pane)
    {
        public object ToSlot() => new
        {
            seat = Seat,
            glyph = IdeDeskView.SeatGlyph(Seat),
            organ = Organ,
            label = IdeDeskView.ShortOrgan(Organ),
            empty = Empty,
            ok = Ok,
            line = Line,
            full = Full
        };

        public object ToCard(bool includePane) => includePane
            ? new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full,
                pane = Pane
            }
            : new
            {
                seat = Seat,
                organ = Organ,
                empty = Empty,
                ok = Ok,
                line = Line,
                full = Full
            };
    }

    sealed class Locus(
        string Id,
        string Kind,
        string Pulse,
        string Drill,
        string? Go = null,
        object? Detail = null)
    {
        public string Id { get; } = Id;
        public string Kind { get; } = Kind;
        public string Pulse { get; } = Pulse;
        public string Drill { get; } = Drill;
        public string? Go { get; } = Go;
        public object? Detail { get; } = Detail;

        public object Card() => new
        {
            id = Id,
            kind = Kind,
            pulse = Pulse,
            drill = Drill,
            go = Go
        };
    }

}
