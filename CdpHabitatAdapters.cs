#nullable enable
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// ADR-0219 L2a — адаптеры портов окружения wake-плоскости (L1) к текущим статик-фасадам.
/// Переходный период: компоненты-экземпляры зависят только от портов, фасады делегируют.
/// </summary>

/// <summary>Порт state root над профилем хабитата (ADR-0199).</summary>
internal sealed class ProfileStateRootProvider : IStateRootProvider
{
    public string ResolveStateRoot() => CdpProfile.StateRoot;
}

/// <summary>Порт ростера над статик-реестром witdb (ADR-0212).</summary>
internal sealed class IntercomAgentsRoster : IAgentRoster
{
    public CideIntercomAgents.AgentRow? Resolve(string nick) =>
        CideIntercomAgents.Resolve(nick);

    public CideIntercomAgents.AgentRow? ResolveDefaultSeat(
        out IReadOnlyList<CideIntercomAgents.AgentRow> liveCandidates) =>
        CideIntercomAgents.ResolveDefaultSeat(out liveCandidates);

    public IReadOnlyList<CideIntercomAgents.AgentRow> Roster() =>
        CideIntercomAgents.Roster();

    public CideIntercomAgents.AgentRow? Claim(
        string nick, string kind, string? lineId, string harness, string? session) =>
        CideIntercomAgents.Claim(nick, kind, lineId, harness, session);
}

/// <summary>Порт коллекций cdp-state.witdb, привязанный к state root на композиции.</summary>
internal sealed class WitDbCdpStateStore : ICdpStateStore
{
    private readonly string _stateRoot;

    public WitDbCdpStateStore(IStateRootProvider stateRoot)
        => _stateRoot = stateRoot.ResolveStateRoot();

    public bool EnqueueWake(CdpWakeEnvelopeEntity row) =>
        CdpStateStore.EnqueueWake(_stateRoot, row);

    public IReadOnlyList<CdpWakeEnvelopeEntity> LoadWake(string? state = null, int limit = 200) =>
        CdpStateStore.LoadWake(_stateRoot, state, limit);

    public bool SetWakeState(string id, string state, string? detail = null, string? skippedReason = null) =>
        CdpStateStore.SetWakeState(_stateRoot, id, state, detail, skippedReason);

    public CdpQueueStateEntity? LoadQueueState(string id) =>
        CdpStateStore.LoadQueueState(_stateRoot, id);

    public bool SetQueueState(CdpQueueStateEntity row) =>
        CdpStateStore.SetQueueState(_stateRoot, row);

    public IReadOnlyList<CdpWakeSubscriptionEntity> LoadSubscriptions() =>
        CdpStateStore.LoadSubscriptions(_stateRoot);

    public bool UpsertSubscription(CdpWakeSubscriptionEntity sub) =>
        CdpStateStore.UpsertSubscription(_stateRoot, sub);

    public int DeleteSubscriptions(string? subId, string? nick, string? eventKind) =>
        CdpStateStore.DeleteSubscriptions(_stateRoot, subId, nick, eventKind);

    public IReadOnlyList<CdpIgniteArmEntity> LoadArms(string seat) =>
        CdpStateStore.LoadArms(_stateRoot, seat);

    public bool ReplaceArms(string seat, IEnumerable<CdpIgniteArmEntity> rows) =>
        CdpStateStore.ReplaceArms(_stateRoot, seat, rows);
}