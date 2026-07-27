#nullable enable
namespace CdpMcp.Cockpit.Instrument;

/// <summary>Named instrument deck at a semantic attention anchor (CIDE ADR 0063).</summary>
public readonly record struct InstrumentDeckDescriptor(
    string DeckId,
    string SemanticAnchorId,
    string LayoutPattern,
    IReadOnlyList<string> OrderedInstrumentIds);

/// <summary>Desk seat → organ mounts (parity with CIDE instrument mount registry; TUI seats, not Avalonia).</summary>
public sealed class DeskInstrumentMountRegistry
{
    readonly object _gate = new();
    readonly Dictionary<string, string> _seatToOrgan = new(StringComparer.OrdinalIgnoreCase);

    public void SyncFromSeats(IReadOnlyDictionary<string, string?> snapshot, IReadOnlyList<string> seatOrder)
    {
        lock (_gate)
        {
            _seatToOrgan.Clear();
            foreach (var seat in seatOrder)
            {
                if (snapshot.TryGetValue(seat, out var organ) && !string.IsNullOrWhiteSpace(organ))
                    _seatToOrgan[seat] = organ!;
            }
        }
    }

    public InstrumentDeckDescriptor Describe(string deckId = "desk-seats", string anchor = "seats", string layout = "seats-row")
    {
        lock (_gate)
        {
            var ids = _seatToOrgan
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => kv.Key + ":" + kv.Value)
                .ToArray();
            return new InstrumentDeckDescriptor(deckId, anchor, layout, ids);
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
            return new Dictionary<string, string>(_seatToOrgan, StringComparer.OrdinalIgnoreCase);
    }
}

public static class DeskInstrumentHost
{
    static readonly Lazy<DeskInstrumentMountRegistry> Reg = new(() => new DeskInstrumentMountRegistry());
    public static DeskInstrumentMountRegistry Current => Reg.Value;
}
