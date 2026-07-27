#nullable enable
namespace CdpMcp.Cockpit.DataBus;

/// <summary>
/// Typed in-process event bus for desk domain signals (CIDE ADR 0099 parity; no Avalonia).
/// </summary>
public interface IDataBus
{
    void Publish<TEvent>(TEvent evt);

    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}
