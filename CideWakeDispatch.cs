#nullable enable
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// ADR-0213 WakeDispatcher — фасад. SSOT-логика живёт в инстансе
/// <see cref="CdpWakeDispatcher"/> (ADR-0219 L2a/2): стор — ICdpStateStore (witdb),
/// рантайм-состояние (single-flight, cooldown, gates) — в инстансе.
/// Композиция реального графа — Lazy ниже (composition root: CdpServiceHost.WarmDefault).
/// Запрет на новые статик-мутации и override-хуки (ADR-0219 §DI.5); каналы получают
/// диспетчер параметром (DI-путь, тестовый шов удалён 2026-09-09).
/// </summary>
internal static class CideWakeDispatch
{
    public const string Schema = "wake_dispatch/v1";
    public const string KindLetter = "letter";
    public const string KindBuildFinished = "build_finished";
    public const string KindTestFinished = "test_finished";
    public const string KindShellFinished = "shell_finished";
    public const string KindPeerShip = "peer_ship";
    public const string KindRemount = "remount";

    static readonly Lazy<CdpWakeDispatcher> _default = new(BuildDefault);

    public static CdpWakeDispatcher Default => _default.Value;

    /// <summary>Явная сборка дефолтного графа (composition root зовёт на старте).</summary>
    public static void WarmDefault() => _ = _default.Value;

    static CdpWakeDispatcher BuildDefault() => new(
        new IntercomAgentsRoster(),
        RealOpencodeWakeTransport.Instance,
        new WitDbCdpStateStore(new ProfileStateRootProvider()),
        stateRoot: new ProfileStateRootProvider());

    public static string StorePath =>
        Path.Combine(new ProfileStateRootProvider().ResolveStateRoot(), CdpStateStore.FileName);

    // --- In (продюсеры) ---

    public static WakeEnvelope? Enqueue(
        string kind, string body,
        string? nick = null, string? session = null, string? harness = null,
        string? from = null, string? task = null)
        => ToEnvelope(Default.Enqueue(kind, body, nick, session, harness, from, task));

    // --- NotificationCenter ---

    public static Subscription? Subscribe(string nick, string eventKind, string? taskFilter = null)
    {
        var sub = Default.Subscribe(nick, eventKind, taskFilter);
        return sub is null ? null : ToSubscription(sub);
    }

    public static int Unsubscribe(string? subId, string? nick, string? eventKind) =>
        Default.Unsubscribe(subId, nick, eventKind);

    public static IReadOnlyList<Subscription> Subscriptions() =>
        Default.Subscriptions().Select(ToSubscription).ToList();

    /// <summary>NotificationCenter entry: event-продюсер (build/test/shell/peer_ship) зовёт это.</summary>
    public static void NotifyEvent(string eventKind, bool ok, string? pulse = null, string? detail = null) =>
        Default.NotifyEvent(eventKind, ok, pulse, detail);

    // --- Управление ---

    public static bool Stopped => Default.Stopped;

    public static void SetStopped(bool stopped) => Default.SetStopped(stopped);

    public static void SetCdtEnabled(bool enabled) => Default.SetCdtEnabled(enabled);

    // --- Dispatch ---

    public static Task TickAsync(CancellationToken ct) => Default.TickAsync(ct);

    // --- Модель (публичный контракт, совместимость) ---

    public sealed class WakeEnvelope
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Kind { get; set; } = KindLetter;
        public string? Nick { get; set; }
        public string? Session { get; set; }
        public string? Harness { get; set; }
        public string Body { get; set; } = "";
        public string? Task { get; set; }
        public string? From { get; set; }
        public string State { get; set; } = "pending";
        public string? SkippedReason { get; set; }
        public string? Detail { get; set; }
        public DateTimeOffset StampedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DeliveredUtc { get; set; }
    }

    /// <summary>NotificationCenter подписка: ник хочет события kind (опц. фильтр по task-префиксу).</summary>
    public sealed class Subscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Nick { get; set; } = "";
        public string EventKind { get; set; } = KindBuildFinished;
        public string? TaskFilter { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    static WakeEnvelope? ToEnvelope(CdpWakeEnvelopeEntity? e) => e is null ? null : new WakeEnvelope
    {
        Id = e.Id,
        Kind = e.Kind,
        Nick = e.Nick,
        Session = e.Session,
        Harness = e.Harness,
        Body = e.Body,
        Task = e.TaskKey,
        From = e.From,
        State = e.State,
        SkippedReason = e.SkippedReason,
        Detail = e.Detail,
        StampedUtc = e.StampedUtc,
        DeliveredUtc = e.DeliveredUtc,
    };

    static Subscription ToSubscription(CdpWakeSubscriptionEntity s) => new()
    {
        Id = s.Id,
        Nick = s.Nick,
        EventKind = s.EventKind,
        TaskFilter = s.TaskFilter,
        CreatedUtc = s.CreatedUtc,
    };
}