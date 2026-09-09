#nullable enable
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// ADR-0219 L2a — port adapters wiring the L1 environment interfaces to the composed
/// instances. Transitional period: components depend on ports only; facades delegate.
/// </summary>

/// <summary>Порт state root над профилем хабитата (ADR-0199).</summary>
internal sealed class ProfileStateRootProvider : IStateRootProvider
{
    public string ResolveStateRoot() => CdpProfile.StateRoot;
}

/// <summary>Порт ростера над скомпонованным инстансом реестра (ADR-0212).</summary>
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

/// <summary>Порт коллекций cdp-state.witdb, привязанный к state root на композиции
/// (ADR-0219 §DI.4: инстанс стора собирается один раз от провайдера root).</summary>
internal sealed class WitDbCdpStateStore : ICdpStateStore
{
    private readonly CdpStateStore _db;

    public WitDbCdpStateStore(IStateRootProvider stateRoot)
        => _db = new CdpStateStore(stateRoot.ResolveStateRoot());

    public bool EnqueueWake(CdpWakeEnvelopeEntity row) => _db.EnqueueWake(row);

    public IReadOnlyList<CdpWakeEnvelopeEntity> LoadWake(string? state = null, int limit = 200) =>
        _db.LoadWake(state, limit);

    public bool SetWakeState(string id, string state, string? detail = null, string? skippedReason = null) =>
        _db.SetWakeState(id, state, detail, skippedReason);

    public int PurgeWake(string state, int keep) => _db.PurgeWake(state, keep);

    public CdpQueueStateEntity? LoadQueueState(string id) => _db.LoadQueueState(id);

    public bool SetQueueState(CdpQueueStateEntity row) => _db.SetQueueState(row);

    public IReadOnlyList<CdpWakeSubscriptionEntity> LoadSubscriptions() => _db.LoadSubscriptions();

    public bool UpsertSubscription(CdpWakeSubscriptionEntity sub) => _db.UpsertSubscription(sub);

    public int DeleteSubscriptions(string? subId, string? nick, string? eventKind) =>
        _db.DeleteSubscriptions(subId, nick, eventKind);

    public IReadOnlyList<CdpIgniteArmEntity> LoadArms(string seat) => _db.LoadArms(seat);

    public bool ReplaceArms(string seat, IEnumerable<CdpIgniteArmEntity> rows) =>
        _db.ReplaceArms(seat, rows);

    public bool SyncArms(string seat, IEnumerable<CdpIgniteArmEntity> rows, IReadOnlyCollection<string> dropIds) =>
        _db.SyncArms(seat, rows, dropIds);
}
