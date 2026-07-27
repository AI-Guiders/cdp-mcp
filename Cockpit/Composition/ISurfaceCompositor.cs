#nullable enable
namespace CdpMcp.Cockpit.Composition;

/// <summary>Generic surface compositor contract (CIDE ADR 0036).</summary>
public interface ISurfaceCompositor<TScene, TPayload, TDecision, TResult>
{
    TResult Compose(TScene scene, TPayload payload, in TDecision decision);
}
