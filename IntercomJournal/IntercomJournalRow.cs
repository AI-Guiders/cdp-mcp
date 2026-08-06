#nullable enable

namespace Cdp.IntercomJournal;

/// <summary>Radio Virtual History row — shared habitat + Glass wire (not IntercomService transport).</summary>
public sealed class IntercomJournalRow
{
    public string Id { get; set; } = "";
    public string FromSeat { get; set; } = "";
    public string ToSeat { get; set; } = "";
    public string Body { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? Channel { get; set; }
    public DateTimeOffset StampedUtc { get; set; }
    public bool Acked { get; set; }
}
