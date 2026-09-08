#nullable enable
using System.Collections.Concurrent;
using Cdp.CdpState;

namespace CdpMcp;

/// <summary>
/// ADR-0219 L2a/2 — WakeDispatcher-инстанс (пилот DI-конвертации).
/// Логика перенесена 1:1 из статика CideWakeDispatch; стор — <see cref="ICdpStateStore"/>
/// (witdb, миграция legacy — в CdpStateStore). Рантайм-состояние (single-flight,
/// cooldown, per-session gates) живёт здесь, не в статиках. In — только Enqueue
/// (письма линий, lifecycle-события, remount); Dispatch — один тик single-flight;
/// Out — тонкий транспорт <see cref="IOpencodeWakeTransport"/>.
/// </summary>
internal sealed class CdpWakeDispatcher
{
    public const string KindLetter = "letter";
    public const string KindBuildFinished = "build_finished";
    public const string KindTestFinished = "test_finished";
    public const string KindShellFinished = "shell_finished";
    public const string KindPeerShip = "peer_ship";
    public const string KindRemount = "remount";

    const string QueueStateId = "wake";

    readonly IAgentRoster _roster;
    readonly IOpencodeWakeTransport _transport;
    readonly ICdpStateStore _store;
    readonly IStateRootProvider? _stateRoot;
    readonly TimeProvider _time;
    readonly CdpHabitatOptions _options;

    int _busy;
    DateTimeOffset _lastDeliveryUtc;
    readonly Dictionary<string, DateTimeOffset> _lastDeliveryByNick = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionGates = new();

    public CdpWakeDispatcher(
        IAgentRoster roster,
        IOpencodeWakeTransport transport,
        ICdpStateStore store,
        IStateRootProvider? stateRoot = null,
        TimeProvider? time = null,
        CdpHabitatOptions? options = null)
    {
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _stateRoot = stateRoot;
        _time = time ?? TimeProvider.System;
        _options = options ?? new CdpHabitatOptions();
    }

    // --- In (продюсеры) ---

    public CdpWakeEnvelopeEntity? Enqueue(
        string kind, string body,
        string? nick = null, string? session = null, string? harness = null,
        string? from = null, string? task = null)
    {
        try
        {
            var envelope = new CdpWakeEnvelopeEntity
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Kind = kind,
                Nick = nick ?? "",
                Body = body,
                Session = session,
                Harness = harness,
                From = from,
                TaskKey = task,
                StampedUtc = _time.GetUtcNow(),
            };

            // Очередь переполнена — падение старейшего pending (best effort, не тишина)
            var pending = _store.LoadWake("pending", _options.MaxPending + 1);
            if (pending.Count >= _options.MaxPending)
            {
                var oldest = pending[0];
                _ = _store.SetWakeState(oldest.Id, "skipped", null, "queue_overflow");
            }

            return _store.EnqueueWake(envelope) ? envelope : null;
        }
        catch
        {
            return null;
        }
    }

    // --- NotificationCenter (ADR-0213): ник → подписка на события kind ---

    public CdpWakeSubscriptionEntity? Subscribe(string nick, string eventKind, string? taskFilter = null)
    {
        if (string.IsNullOrWhiteSpace(nick) || string.IsNullOrWhiteSpace(eventKind))
            return null;

        var sub = new CdpWakeSubscriptionEntity
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Nick = nick.Trim(),
            EventKind = NormalizeEvent(eventKind),
            TaskFilter = string.IsNullOrWhiteSpace(taskFilter) ? null : taskFilter.Trim(),
            CreatedUtc = _time.GetUtcNow(),
        };

        try
        {
            // idempotent: same nick+kind+filter уже подписан
            var dup = _store.LoadSubscriptions().FirstOrDefault(s =>
                s.Nick.Equals(sub.Nick, StringComparison.OrdinalIgnoreCase)
                && s.EventKind.Equals(sub.EventKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.TaskFilter, sub.TaskFilter, StringComparison.OrdinalIgnoreCase));
            if (dup is not null)
            {
                sub.Id = dup.Id;
                return sub;
            }

            return _store.UpsertSubscription(sub) ? sub : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Отписать: по id, или ник+kind (или всё по нику, когда kind пуст).</summary>
    public int Unsubscribe(string? subId, string? nick, string? eventKind)
    {
        try
        {
            return _store.DeleteSubscriptions(
                subId,
                nick,
                eventKind is null ? null : NormalizeEvent(eventKind));
        }
        catch
        {
            return 0;
        }
    }

    public IReadOnlyList<CdpWakeSubscriptionEntity> Subscriptions() =>
        _store.LoadSubscriptions();

    /// <summary>
    /// NotificationCenter entry: event-продюсер (build/test/shell/peer_ship) зовёт это;
    /// диспетчер матчит подписки и кладёт персональные envelope в очередь.
    /// </summary>
    public void NotifyEvent(string eventKind, bool ok, string? pulse = null, string? detail = null)
    {
        try
        {
            var ev = NormalizeEvent(eventKind);
            var subs = _store.LoadSubscriptions()
                .Where(s => s.EventKind.Equals(ev, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var sub in subs)
            {
                if (!string.IsNullOrWhiteSpace(sub.TaskFilter)
                    && detail is not null
                    && !detail.Contains(sub.TaskFilter, StringComparison.OrdinalIgnoreCase)
                    && !ev.Contains(sub.TaskFilter, StringComparison.OrdinalIgnoreCase))
                    continue; // фильтр по task-подстроке

                _ = Enqueue(
                    ev,
                    $"{ev}: {(ok ? "ok" : "FAIL")} — {pulse ?? ""}{(detail is null ? "" : " · " + detail)}",
                    nick: sub.Nick,
                    from: "NotificationCenter",
                    task: sub.TaskFilter ?? sub.EventKind);
            }
        }
        catch
        {
            /* NotificationCenter — best effort: событие не должно ломать продюсер */
        }
    }

    // --- Управление (SSOT, queue_state) ---

    public bool Stopped => _store.LoadQueueState(QueueStateId)?.Stopped ?? false;

    public void SetStopped(bool stopped)
    {
        var qs = _store.LoadQueueState(QueueStateId) ?? new CdpQueueStateEntity { Id = QueueStateId };
        qs.Stopped = stopped;
        qs.StampedUtc = _time.GetUtcNow();
        _ = _store.SetQueueState(qs);
    }

    public void SetCdtEnabled(bool enabled)
    {
        var qs = _store.LoadQueueState(QueueStateId) ?? new CdpQueueStateEntity { Id = QueueStateId };
        qs.HarnessCdt = enabled;
        qs.StampedUtc = _time.GetUtcNow();
        _ = _store.SetQueueState(qs);
    }

    static string NormalizeEvent(string eventName)
    {
        var s = eventName.Trim().ToLowerInvariant();
        return s switch
        {
            "build" => KindBuildFinished,
            "test" => KindTestFinished,
            "shell" => KindShellFinished,
            "ship" => KindPeerShip,
            "letter" => KindLetter,
            _ => s
        };
    }

    // --- Dispatch (один тик) ---

    public async Task TickAsync(CancellationToken ct)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;
        try
        {
            await TickCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _busy, 0);
        }
    }

    async Task TickCoreAsync(CancellationToken ct)
    {
        var qs = _store.LoadQueueState(QueueStateId);
        if (qs?.Stopped ?? false)
            return;

        // Legacy-совместимость: старые arms/line-*.json (fanout v0) конвертируются в очередь.
        AbsorbLegacyArms();

        var harnessCdt = qs?.HarnessCdt ?? false;
        var cooldown = qs?.CooldownSeconds > 0
            ? qs.CooldownSeconds
            : _options.DefaultDeliveryCooldownSeconds;

        foreach (var e in _store.LoadWake("pending", _options.MaxPending).ToList())
        {
            if (ct.IsCancellationRequested)
                return;

            var now = _time.GetUtcNow();
            if (now - _lastDeliveryUtc < TimeSpan.FromSeconds(cooldown))
                return; // cooldown — глобальный тормоз, ноты остаются pending

            // Per-nick cooldown (Света 2026-09-07): второй стук по той же линии ждёт,
            // пока сессия переварит предыдущий ход — иначе гонка run'ов = пустые user-ходы.
            var nickKey = e.Nick?.Trim() ?? "";
            if (nickKey.Length > 0
                && _lastDeliveryByNick.TryGetValue(nickKey, out var lastNick)
                && now - lastNick < TimeSpan.FromSeconds(_options.WakeNickCooldownSeconds))
                continue;

            var result = await DeliverAsync(e, harnessCdt, ct).ConfigureAwait(false);

            if (result.State == "delivered")
            {
                _lastDeliveryUtc = now;
                if (nickKey.Length > 0)
                    _lastDeliveryByNick[nickKey] = now;
            }

            _ = _store.SetWakeState(result.Id, result.State, result.Detail, result.SkippedReason);

            // хвост завершённых — не копим (per-state cap ≈ KeepCompleted)
            if (result.State is "delivered" or "skipped" or "failed")
                _ = _store.PurgeWake(result.State, _options.KeepCompleted);

            if (result.State == "pending" && result.SkippedReason is "no_registry" or "no_session")
                break; // цель не готова — не долбим остальные (тот же реестр)
        }
    }

    async Task<CdpWakeEnvelopeEntity> DeliverAsync(CdpWakeEnvelopeEntity e, bool harnessCdt, CancellationToken ct)
    {
        // Wake hygiene (Света 2026-09-06): пустое тело будит линию пустым user-ходом,
        // самостук (From == Nick) — самоэхо. Оба не доставляются — честный skip.
        if (string.IsNullOrWhiteSpace(e.Body))
        {
            e.State = "skipped";
            e.SkippedReason = "empty_body";
            return e;
        }
        if (!string.IsNullOrWhiteSpace(e.From)
            && !string.IsNullOrWhiteSpace(e.Nick)
            && e.From!.Equals(e.Nick, StringComparison.OrdinalIgnoreCase))
        {
            e.State = "skipped";
            e.SkippedReason = "self_echo";
            return e;
        }

        var harness = e.Harness?.ToLowerInvariant();
        var session = e.Session;

        if (harness is null && !string.IsNullOrWhiteSpace(e.Nick))
        {
            var agent = _roster.Resolve(e.Nick);
            if (agent is null)
            {
                e.SkippedReason = "no_registry";
                return e; // pending — линия ещё не замечена сессией
            }

            harness = agent.Harness.ToLowerInvariant();
            session = agent.Session;
        }

        switch (harness)
        {
            case "opencode":
                if (string.IsNullOrWhiteSpace(session))
                {
                    e.SkippedReason = "no_session";
                    return e; // pending — session не привязан
                }

                // Вежливый почтальон (Света 2026-09-07): письмо перебило генерацию Ток/Тень —
                // ждём «exiting loop», конверт остаётся pending до завершения хода.
                if (_transport.IsSessionBusy(session!))
                {
                    e.Detail = "session_busy — письмо ждёт завершения хода";
                    return e; // pending — ретрай на следующем тике
                }

                // Класс-B устранение гонки run'ов (Света 2026-09-08): per-session gate —
                // один in-flight wake на линию, busy-проверка и отправка атомарны.
                var wakeGate = _sessionGates.GetOrAdd(session!, _ => new SemaphoreSlim(1, 1));
                await wakeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_transport.IsSessionBusy(session!))
                    {
                        e.Detail = "session_busy (re-check inside gate) — письмо ждёт завершения хода";
                        return e; // pending — ретрай на следующем тике
                    }

                    var cli = await _transport
                        .SendCliAsync(session!, e.Body, ct).ConfigureAwait(false);
                    if (CideWakeChannels.IsOk(cli))
                    {
                        e.State = "delivered";
                        e.DeliveredUtc = _time.GetUtcNow();
                        e.Detail = "cli";
                        return e;
                    }

                    e.Detail = $"cli_failed: {DetailOf(cli)}";
                    return e; // pending — ретрай на следующем тике
                }
                finally
                {
                    wakeGate.Release();
                }

            case "citizen":
                // Stage-2: citizen-turn канал; пока честный skip, не тишина
                e.State = "skipped";
                e.SkippedReason = "citizen_channel_todo";
                return e;

            case "cursor":
                if (!harnessCdt)
                {
                    e.State = "skipped";
                    e.SkippedReason = "cdt_disabled (region)";
                }
                else
                {
                    e.State = "skipped";
                    e.SkippedReason = "cdt_channel_todo";
                }
                return e;

            default:
                e.State = "failed";
                e.SkippedReason = "unknown_harness";
                return e;
        }
    }

    void AbsorbLegacyArms()
    {
        if (_stateRoot is null)
            return;
        try
        {
            var arms = Path.Combine(_stateRoot.ResolveStateRoot(), "arms");
            var done = Path.Combine(arms, "done");
            Directory.CreateDirectory(done);
            foreach (var path in Directory.GetFiles(arms, "line-*.json"))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var note = System.Text.Json.JsonSerializer.Deserialize<LegacyArmNote>(json, LegacyOpts);
                    File.Move(path, Path.Combine(done, Path.GetFileName(path)), overwrite: true);
                    if (note is null || !string.IsNullOrWhiteSpace(note.Body))
                    {
                        _ = Enqueue(KindLetter,
                            note?.Body ?? "",
                            nick: note?.Nick,
                            from: note?.From,
                            task: "legacy_arms");
                    }
                }
                catch
                {
                    /* best effort */
                }
            }
        }
        catch
        {
            /* best effort */
        }
    }

    sealed class LegacyArmNote
    {
        public string? Nick { get; set; }
        public string? From { get; set; }
        public string? Body { get; set; }
    }

    static readonly System.Text.Json.JsonSerializerOptions LegacyOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    static string DetailOf(object result) =>
        result.GetType().GetProperty("error")?.GetValue(result) as string
        ?? result.GetType().GetProperty("detail")?.GetValue(result) as string
        ?? "unknown";
}