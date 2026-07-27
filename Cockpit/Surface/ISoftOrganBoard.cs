#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>Surface contract: SoftOrganKind → board payload (Ide* Handle stays behind implementer).</summary>
public readonly record struct SoftOrganBoardHit(
    object Board,
    string? Pulse = null,
    string? Schema = null);

public interface ISoftOrganBoard
{
    SoftOrganBoardHit Build(SoftOrganKind kind);
}
