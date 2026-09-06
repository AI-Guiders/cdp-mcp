#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CdpMcp;

/// <summary>
/// ADR-0213 WakeDispatcher — MessageBroker для wake-доставки.
/// In (продюсеры) — только Enqueue: письма линий (mention-wake), lifecycle-события
/// (build/test/shell/peer_ship), remount. Продюсер ничего не знает про доставку.
/// Dispatch — один тик (single-flight): drain очереди → резолв цели по реестру →
/// канал → статус обратно. Тормоза внутри: cooldown, стоп, лимит очереди.
/// Out (каналы) — тонкие адаптеры (CideWakeChannels): opencode CLI/HTTP, citizen, cursor(opt-in).
/// Стор — один SSOT-файл wake-dispatch.json (атомарная tmp+move запись).
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

    static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static int _busy;
    static DateTimeOffset _lastDeliveryUtc;

    public static string StorePath =>
        Path.Combine(CideIntercomVoiceLatch.StateRoot, "wake-dispatch.json");

    static string TmpPath =>
        StorePath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";

    // --- модель ---

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

    public sealed class DispatchDoc
    {
                public string Schema { get; set; } = CideWakeDispatch.Schema;
        public bool Stopped { get; set; }
        public int DeliveryCooldownSeconds { get; set; } = 15;
        public bool HarnessCdt { get; set; }
        public int MaxPending { get; set; } = 200;
        public int KeepCompleted { get; set; } = 100;
        public List<WakeEnvelope> Queue { get; set; } = new();
        public List<Subscription> Subscriptions { get; set; } = new();
    }

    // --- стор ---

    public static DispatchDoc TryRead()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new DispatchDoc();
                        var raw = File.ReadAllText(StorePath);
            var doc = JsonSerializer.Deserialize<DispatchDoc>(raw, ReadOpts);
            if (doc is null)
                return new DispatchDoc();
            // старые сторы без новых полей → null-коллекции; нормализуем
            doc.Queue ??= new List<WakeEnvelope>();
            doc.Subscriptions ??= new List<Subscription>();
            return doc;
        }
        catch
        {
            return new DispatchDoc();
        }
    }

    static void Save(DispatchDoc doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var json = JsonSerializer.Serialize(doc, WriteOpts);
        File.WriteAllText(TmpPath, json);
        File.Move(TmpPath, StorePath, overwrite: true);
    }

    // --- In (продюсеры) ---

    public static WakeEnvelope? Enqueue(
        string kind, string body,
        string? nick = null, string? session = null, string? harness = null,
        string? from = null, string? task = null)
    {
        try
        {
            var envelope = new WakeEnvelope
            {
                Kind = kind,
                Body = body,
                Nick = nick,
                Session = session,
                Harness = harness,
                From = from,
                Task = task
            };
            Save(Apply(TryRead(), doc =>
            {
                if (doc.Queue.Count(x => x.State == "pending") >= doc.MaxPending)
                {
                    // очередь переполнена — падение старейшего pending (best effort, не тишина)
                    var oldest = doc.Queue.FirstOrDefault(x => x.State == "pending");
                    if (oldest is not null)
                    {
                        oldest.State = "skipped";
                        oldest.SkippedReason = "queue_overflow";
                    }
                }
                doc.Queue.Add(envelope);
                return doc;
            }));
            return envelope;
        }
        catch
        {
            return null;
        }
    }

    // --- NotificationCenter (Света 2026-09-06): ник → подписка на события kind ---

    /// <summary>Подписать ник на события kind (опц. фильтр task-подстроки). Idempotent.</summary>
    public static Subscription? Subscribe(string nick, string eventKind, string? taskFilter = null)
    {
        if (string.IsNullOrWhiteSpace(nick) || string.IsNullOrWhiteSpace(eventKind))
            return null;

        var sub = new Subscription
        {
            Nick = nick.Trim(),
            EventKind = NormalizeEvent(eventKind),
            TaskFilter = string.IsNullOrWhiteSpace(taskFilter) ? null : taskFilter.Trim()
        };

        try
        {
            Save(Apply(TryRead(), doc =>
            {
                // idempotent: same nick+kind+filter уже подписан
                var dup = doc.Subscriptions.FirstOrDefault(s =>
                    s.Nick.Equals(sub.Nick, StringComparison.OrdinalIgnoreCase)
                    && s.EventKind.Equals(sub.EventKind, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.TaskFilter, sub.TaskFilter, StringComparison.OrdinalIgnoreCase));
                if (dup is not null)
                {
                    sub.Id = dup.Id;
                    return doc;
                }
                doc.Subscriptions.Add(sub);
                return doc;
            }));
            return sub;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Отписать: по id, или ник+kind (или всё по нику, когда kind пуст).</summary>
    public static int Unsubscribe(string? subId, string? nick, string? eventKind)
    {
        try
        {
            var removed = 0;
            Save(Apply(TryRead(), doc =>
            {
                var doomed = doc.Subscriptions.Where(s =>
                    (subId is not null && s.Id.Equals(subId.Trim(), StringComparison.OrdinalIgnoreCase))
                    || (nick is not null
                        && s.Nick.Equals(nick.Trim(), StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(eventKind)
                            || s.EventKind.Equals(NormalizeEvent(eventKind), StringComparison.OrdinalIgnoreCase))))
                    .ToList();
                foreach (var d in doomed)
                    doc.Subscriptions.Remove(d);
                removed = doomed.Count;
                return doc;
            }));
            return removed;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Активные подписки (для op=subs / отладки).</summary>
    public static IReadOnlyList<Subscription> Subscriptions() =>
        TryRead().Subscriptions;

    /// <summary>
    /// NotificationCenter entry: event-продюсер (build/test/shell/peer_ship) зовёт это;
    /// диспетчер матчит подписки и кладёт персональные envelope в очередь.
    /// </summary>
    public static void NotifyEvent(string eventKind, bool ok, string? pulse = null, string? detail = null)
    {
        try
        {
            var ev = NormalizeEvent(eventKind);
            var doc = TryRead();
            var subs = doc.Subscriptions
                .Where(s => s.EventKind.Equals(ev, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var sub in subs)
            {
                if (!string.IsNullOrWhiteSpace(sub.TaskFilter)
                    && detail is not null
                    && !detail.Contains(sub.TaskFilter, StringComparison.OrdinalIgnoreCase)
                    && !ev.Contains(sub.TaskFilter, StringComparison.OrdinalIgnoreCase))
                    continue; // фильтр по task-подстроке

                Enqueue(
                    ev,
                    $"{ev}: {(ok ? "ok" : "FAIL")} — {pulse ?? ""}{(detail is null ? "" : " · " + detail)}",
                    nick: sub.Nick,
                    from: "NotificationCenter",
                    task: sub.TaskFilter ?? sub.EventKind);
            }
        }
        catch
        {
            /*NotificationCenter — best effort: событие не должно ломать продюсер*/
        }
    }

    // --- управление (SSOT, не файлы) ---

    public static bool Stopped => TryRead().Stopped;

    public static void SetStopped(bool stopped) =>
        Save(Apply(TryRead(), doc => { doc.Stopped = stopped; return doc; }));

    public static void SetCdtEnabled(bool enabled) =>
        Save(Apply(TryRead(), doc => { doc.HarnessCdt = enabled; return doc; }));

    /// <summary>Normalize event kind: lowercase + aliases (build→build_finished и т.п.).</summary>
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

    public static async Task TickAsync(CancellationToken ct)
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

    static async Task TickCoreAsync(CancellationToken ct)
    {
        var doc = TryRead();
        if (doc.Stopped)
            return;

        // Legacy-совместимость: старые arms/line-*.json (fanout v0) конвертируются в очередь.
        AbsorbLegacyArms(doc);

        foreach (var e in doc.Queue.Where(x => x.State == "pending").ToList())
        {
            if (ct.IsCancellationRequested)
                return;

            var now = DateTimeOffset.UtcNow;
            if (now - _lastDeliveryUtc < TimeSpan.FromSeconds(doc.DeliveryCooldownSeconds))
                return; // cooldown — глобальный тормоз, ноты остаются pending

            var result = await DeliverAsync(e, doc, ct).ConfigureAwait(false);
            if (result.State == "delivered")
                _lastDeliveryUtc = now;

            var state = result.State;
            Save(Apply(doc, d =>
            {
                var q = d.Queue.FirstOrDefault(x => x.Id == result.Id);
                if (q is null)
                {
                    d.Queue.Add(result);
                    return d;
                }
                q.State = result.State;
                q.SkippedReason = result.SkippedReason;
                q.Detail = result.Detail;
                q.DeliveredUtc = result.DeliveredUtc;
                // не копим хвост вечно
                var completed = d.Queue.Where(x => x.State != "pending").ToList();
                if (completed.Count > d.KeepCompleted)
                {
                    foreach (var dead in completed.Take(completed.Count - d.KeepCompleted))
                        d.Queue.Remove(dead);
                }
                return d;
            }));
            if (state == "pending" && result.SkippedReason is "no_registry" or "no_session")
                break; // цель не готова — не долбим остальные (тот же реестр)
        }
    }

    static async Task<WakeEnvelope> DeliverAsync(WakeEnvelope e, DispatchDoc doc, CancellationToken ct)
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
            var agent = CideIntercomAgents.Resolve(e.Nick);
            if (agent is null)
            {
                e.SkippedReason = "no_registry";
                return e; // pending — линия ещё не засеяна сессией
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
                var cli = await CideWakeChannels.Opencode
                    .SendCliAsync(session!, e.Body, ct).ConfigureAwait(false);
                if (CideWakeChannels.IsOk(cli))
                {
                    e.State = "delivered";
                    e.DeliveredUtc = DateTimeOffset.UtcNow;
                    e.Detail = "cli";
                    return e;
                }
                var url = await CideWakeChannels.Opencode.TryEnsureServerUrlAsync(ct).ConfigureAwait(false);
                if (url is null)
                {
                    e.Detail = $"cli_failed: {DetailOf(cli)}";
                    return e; // pending — ретрай на следующем тике
                }
                var http = await CideWakeChannels.Opencode
                    .SendHttpAsync(url, session!, e.Body, ct).ConfigureAwait(false);
                if (CideWakeChannels.IsOk(http))
                {
                    e.State = "delivered";
                    e.DeliveredUtc = DateTimeOffset.UtcNow;
                    e.Detail = "http";
                }
                else
                {
                    e.State = "failed";
                    e.Detail = DetailOf(http);
                }
                return e;

            case "citizen":
                // Stage-2: citizen-turn канал; пока честный skip, не тишина
                e.State = "skipped";
                e.SkippedReason = "citizen_channel_todo";
                return e;

            case "cursor":
                if (!doc.HarnessCdt)
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

    static void AbsorbLegacyArms(DispatchDoc doc)
    {
        try
        {
            var arms = Path.Combine(CideIntercomVoiceLatch.StateRoot, "arms");
            var done = Path.Combine(arms, "done");
            Directory.CreateDirectory(done);
            foreach (var path in Directory.GetFiles(arms, "line-*.json"))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var note = JsonSerializer.Deserialize<LegacyArmNote>(json, ReadOpts);
                    File.Move(path, Path.Combine(done, Path.GetFileName(path)), overwrite: true);
                    if (note is null || !string.IsNullOrWhiteSpace(note.Body))
                    {
                        Enqueue(KindLetter,
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

    static string DetailOf(object result) =>
        result.GetType().GetProperty("error")?.GetValue(result) as string
        ?? result.GetType().GetProperty("detail")?.GetValue(result) as string
        ?? "unknown";

    static DispatchDoc Apply(DispatchDoc doc, Func<DispatchDoc, DispatchDoc> f)
    {
        var copy = f(doc);
        // на всякий случай держим схему честной
        copy.Schema = Schema;
        return copy;
    }
}