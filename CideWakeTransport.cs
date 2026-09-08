#nullable enable
namespace CdpMcp;

/// <summary>
/// Транспорт wake-доставки opencode — seam для тестов (NSubstitute, ADR-0219 P1).
/// Подмена в CideWakeChannels.Opencode.Transport убирает реальные run-спавны
/// из тест-контекста (класс «пустых user-ходов»).
/// </summary>
internal interface IOpencodeWakeTransport
{
    bool IsSessionBusy(string session);

    /// <summary>CLI run — единственная точка спавна процесса; в тестах подменяется.</summary>
    Task<object> SendCliAsync(string session, string message, CancellationToken ct);
}

internal sealed class RealOpencodeWakeTransport : IOpencodeWakeTransport
{
    public static readonly RealOpencodeWakeTransport Instance = new();

    public bool IsSessionBusy(string session) =>
        CideWakeChannels.Opencode.IsSessionBusy(session);

    public Task<object> SendCliAsync(string session, string message, CancellationToken ct) =>
        CideWakeChannels.Opencode.SendCliAsync(session, message, ct);
}
