#nullable enable
using System.Text;

namespace CdpMcp;

internal static partial class IdeDeskView
{
    static string BuildBanner(IReadOnlyList<Slot> slots)
    {
        var parts = slots.Select(s =>
        {
            var g = SeatGlyph(s.Seat);
            if (s.Empty) return $"{g}:—";
            var mark = s.Ok ? "" : "!";
            return $"{g}:{mark}{ShortOrgan(s.Organ)}";
        });
        return "| " + string.Join(" | ", parts) + " |";
    }

    static string FormatBoardLine(Slot s)
    {
        var g = SeatGlyph(s.Seat).PadRight(1);
        var organ = (s.Empty ? "—" : ShortOrgan(s.Organ)).PadRight(10);
        var flag = s.Empty ? " " : (s.Ok ? "·" : "!");
        return $"{g}  {organ} {flag} {s.Line}";
    }

    static string BuildAscii(IReadOnlyList<Slot> slots)
    {
        // Fixed three columns — Scan Pattern on one “screen”.
        const int col = 22;
        var titles = new[] { "P", "Forward", "M" };
        var bySeat = slots.ToDictionary(s => s.Seat, StringComparer.OrdinalIgnoreCase);

        string Cell(string seat, int row)
        {
            if (!bySeat.TryGetValue(seat, out var s))
                return Pad("", col);
            if (row == 0)
                return Pad(s.Empty ? "(empty)" : ShortOrgan(s.Organ), col);
            if (row == 1)
                return Pad(s.Empty ? "" : TrimLine(s.Line, col - 1), col);
            return Pad(s.Full ? "[full]" : (s.Ok || s.Empty ? "" : "!"), col);
        }

        var sb = new StringBuilder();
        sb.Append('┌').Append(H(col)).Append('┬').Append(H(col)).Append('┬').Append(H(col)).Append('┐').Append('\n');
        sb.Append('│').Append(Pad(titles[0], col)).Append('│').Append(Pad(titles[1], col)).Append('│').Append(Pad(titles[2], col)).Append('│').Append('\n');
        sb.Append('├').Append(H(col)).Append('┼').Append(H(col)).Append('┼').Append(H(col)).Append('┤').Append('\n');
        for (var row = 0; row < 2; row++)
        {
            sb.Append('│').Append(Cell("p", row)).Append('│').Append(Cell("forward", row)).Append('│').Append(Cell("m", row)).Append('│').Append('\n');
        }

        sb.Append('└').Append(H(col)).Append('┴').Append(H(col)).Append('┴').Append(H(col)).Append('┘');
        return sb.ToString();

        static string H(int n) => new string('─', n);
        static string Pad(string s, int n)
        {
            s ??= "";
            if (s.Length > n) return s[..(n - 1)] + "…";
            return s.PadRight(n);
        }
    }

    static string TrimLine(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        if (s.Length <= max) return s;
        return s[..(max - 1)] + "…";
    }
}
