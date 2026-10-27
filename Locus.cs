#nullable enable

using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>
/// Структурированная локация на проводе: то же, что anchor-строка
/// [Kind:CodeEdit; File:…; Member:…; Line:…], только разобранная.
/// Живой источник — <see cref="BracketLocate.Span"/> (Parse/Format);
/// SchemaGen-источник для схем (ToolSchemas).
/// </summary>
internal sealed record Locus(
    string file,
    string? member,
    int? line_start,
    int? line_end)
{
    public static Locus From(BracketLocate.Span span) =>
        new(span.File ?? "", span.MemberKey, span.LineStart, span.LineEnd);

    /// <summary>Разобрать anchor-строку; неопознанный провод → null (строка остаётся единственным носителем).</summary>
    public static Locus? TryFromWire(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire))
            return null;
        try
        {
            var span = BracketLocate.Parse(wire);
            return span.File is null ? null : From(span);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
