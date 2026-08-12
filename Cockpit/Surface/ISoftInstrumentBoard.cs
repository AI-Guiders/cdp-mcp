#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface contract: SoftInstrumentKind → board payload (Ide* Handle stays behind implementer).</summary>
public readonly record struct SoftInstrumentBoardHit(
    object Board,
    string? Pulse = null,
    string? Schema = null);

public interface ISoftInstrumentBoard
{
    SoftInstrumentBoardHit Build(SoftInstrumentKind kind);
}
