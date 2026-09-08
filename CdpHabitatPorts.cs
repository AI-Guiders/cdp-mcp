#nullable enable
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// ADR-0219 L1 — интерфейсы окружения wake-плоскости (пилот: wake dispatcher).
/// Каждый интерфейс — единственная точка зависимости компонента; реальный граф
/// собирает composition root (CdpServiceHost), тесты собирают свой граф.
/// Запрет на новые статик-мутации и override-хуки (ADR-0219 §DI.5).
/// </summary>
internal interface IStateRootProvider
{
    /// <summary>Текущий state root (ADR-0199: env-профиль / client roots / сессия / тенант).</summary>
    string ResolveStateRoot();
}

/// <summary>Chat-room ростер линий — порт над реестром witdb (ADR-0212).</summary>
internal interface IAgentRoster
{
    CideIntercomAgents.AgentRow? Resolve(string nick);

    CideIntercomAgents.AgentRow? ResolveDefaultSeat(
        out IReadOnlyList<CideIntercomAgents.AgentRow> liveCandidates);

    IReadOnlyList<CideIntercomAgents.AgentRow> Roster();

    CideIntercomAgents.AgentRow? Claim(
        string nick, string kind, string? lineId, string harness, string? session);
}

/// <summary>
/// Порт коллекций cdp-state.witdb (ADR-0219): wake-очередь, queue_state,
/// подписки NotificationCenter, ignite-arms. Экземпляр привязан к state root.
/// </summary>
internal interface ICdpStateStore
{
    // --- wake queue (ADR-0213) ---

    bool EnqueueWake(CdpWakeEnvelopeEntity row);

    IReadOnlyList<CdpWakeEnvelopeEntity> LoadWake(string? state = null, int limit = 200);

    bool SetWakeState(string id, string state, string? detail = null, string? skippedReason = null);

    // --- queue state (stopped / cooldown) ---

    CdpQueueStateEntity? LoadQueueState(string id);

    bool SetQueueState(CdpQueueStateEntity row);

    // --- NotificationCenter подписки ---

    IReadOnlyList<CdpWakeSubscriptionEntity> LoadSubscriptions();

    bool UpsertSubscription(CdpWakeSubscriptionEntity sub);

    int DeleteSubscriptions(string? subId, string? nick, string? eventKind);

    // --- ignite arms (per-seat) ---

    IReadOnlyList<CdpIgniteArmEntity> LoadArms(string seat);

    bool ReplaceArms(string seat, IEnumerable<CdpIgniteArmEntity> rows);
}

/// <summary>
/// Тюнинг wake-плоскости — конфиг, компонуемый один раз в composition root.
/// Рантайм-состояние (stopped/cooldown) живёт в queue_state, не здесь.
/// </summary>
internal sealed class CdpHabitatOptions
{
    /// <summary>Минимум между доставками одной линии (вежливый почтальон, 2026-09-07).</summary>
    public int WakeNickCooldownSeconds { get; init; } = 120;

    /// <summary>Лимит pending-конвертов в очереди.</summary>
    public int MaxPending { get; init; } = 200;

    /// <summary>Хвост завершённых конвертов, хранимых в очереди.</summary>
    public int KeepCompleted { get; init; } = 100;

    /// <summary>Глобальная доставка-пауза, если в queue_state ещё не записано значение.</summary>
    public int DefaultDeliveryCooldownSeconds { get; init; } = 15;
}