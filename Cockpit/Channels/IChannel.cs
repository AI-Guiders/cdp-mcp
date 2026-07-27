#nullable enable
namespace CdpMcp.Cockpit.Channels;

/// <summary>Generic channel contract (CIDE ADR 0036): domain context → semantic payload.</summary>
public interface IChannel<TContext, TPayload>
{
    TPayload Build(in TContext context);
}
