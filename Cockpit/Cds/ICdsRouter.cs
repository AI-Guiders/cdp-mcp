#nullable enable
namespace CdpMcp.Cockpit.Cds;

/// <summary>Generic CDS routing contract (CIDE ADR 0036): input → routing decision.</summary>
public interface ICdsRouter<in TInput, out TDecision>
{
    TDecision Route(TInput input);
}
