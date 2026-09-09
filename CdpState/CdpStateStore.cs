#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace Cdp.CdpState;

/// <summary>
/// ADR-0219 P0+P1 — единый state store хабитата: <c>cdp-state.witdb</c> на state root.
/// Коллекции: wake_envelopes (ADR-0213), ignite_arms (per-seat), store_registry (P0),
/// queue_state. Legacy-файлы (wake-dispatch.json, ignite-arms-{seat}.json) импортируются
/// при первом старте (migration-on-first-run) и переименовываются в interop-only .bak.
/// Класс «молчаливой порчи записи» закрывается транзакцией EF + WitDbFileGate.
/// </summary>
public static class CdpStateStoreDb
{
    public const string FileName = "cdp-state.witdb";
    public const string LegacyWakeQueueFileName = "wake-dispatch.json";
    public const string LegacyArmsFilePrefix = "ignite-arms-";
    public const string LegacyArmsFileSuffix = ".json";

    static readonly Lock Gate = new();
    static readonly TimeSpan FileGateWait = TimeSpan.FromSeconds(12);
    static readonly JsonSerializerOptions LegacyOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string DbPath(string stateRoot) => Path.Combine(stateRoot, FileName);
    public static string LegacyWakeQueuePath(string stateRoot) => Path.Combine(stateRoot, LegacyWakeQueueFileName);

    // ---------- P0: store-registry ----------

    public static bool RegisterStore(string stateRoot, string name, string owner, string format, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(name))
            return false;
        return WithDb(stateRoot, db =>
        {
            var row = db.Registry.Find(name.Trim());
            if (row is null)
            {
                db.Registry.Add(new CdpStoreRegistryEntity
                {
                    Name = name.Trim(),
                    Owner = owner?.Trim() ?? "",
                    Format = format?.Trim() ?? "witdb",
                    Note = note,
                    StampedUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                row.Owner = owner?.Trim() ?? row.Owner;
                row.Format = format?.Trim() ?? row.Format;
                row.Note = note ?? row.Note;
                row.StampedUtc = DateTimeOffset.UtcNow;
            }
            db.SaveChanges();
            return true;
        });
    }

    public static IReadOnlyList<CdpStoreRegistryEntity> ListStores(string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.Registry.AsNoTracking().OrderBy(x => x.Name).ToList();
        });
    }

    // ---------- P1: wake queue ----------

    public static bool EnqueueWake(string stateRoot, CdpWakeEnvelopeEntity row)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || row is null || string.IsNullOrWhiteSpace(row.Id))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            if (db.Wake.AsNoTracking().Any(x => x.Id == row.Id))
                return true;
            db.Wake.Add(row);
            db.SaveChanges();
            return true;
        });
    }

    public static IReadOnlyList<CdpWakeEnvelopeEntity> LoadWake(string stateRoot, string? state = null, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var q = db.Wake.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(state))
                q = q.Where(x => x.State == state);
            return q.OrderBy(x => x.StampedUtc).Take(Math.Clamp(limit, 1, 500)).ToList();
        });
    }

    public static bool SetWakeState(string stateRoot, string id, string state, string? detail = null, string? skippedReason = null)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(id))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var row = db.Wake.Find(id);
            if (row is null)
                return false;
            row.State = state;
            row.Detail = detail ?? row.Detail;
            row.SkippedReason = skippedReason ?? row.SkippedReason;
            if (state == "delivered")
                row.DeliveredUtc = DateTimeOffset.UtcNow;
            db.SaveChanges();
            return true;
        });
    }

    public static bool SetWakeStopped(string stateRoot, bool stopped)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var qs = db.QueueState.Find("wake");
            if (qs is null)
            {
                db.QueueState.Add(new CdpQueueStateEntity { Id = "wake", Stopped = stopped, StampedUtc = DateTimeOffset.UtcNow });
            }
            else
            {
                qs.Stopped = stopped;
                qs.StampedUtc = DateTimeOffset.UtcNow;
            }
            db.SaveChanges();
            return true;
        });
    }

    public static bool IsWakeStopped(string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.QueueState.Find("wake")?.Stopped ?? false;
        });
    }
    public static CdpQueueStateEntity? LoadQueueState(string stateRoot, string id)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(id))
            return null;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.QueueState.Find(id);
        });
    }

    public static bool SetQueueState(string stateRoot, CdpQueueStateEntity row)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || row is null || string.IsNullOrWhiteSpace(row.Id))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var qs = db.QueueState.Find(row.Id);
            if (qs is null)
                db.QueueState.Add(row);
            else
            {
                qs.Stopped = row.Stopped;
                qs.CooldownSeconds = row.CooldownSeconds;
                qs.HarnessCdt = row.HarnessCdt;
                qs.StampedUtc = DateTimeOffset.UtcNow;
            }
            db.SaveChanges();
            return true;
        });
    }

    /// <summary>Обрезать хвост завершённых конвертов данного state до keep (ADR-0213 KeepCompleted).</summary>
    public static int PurgeWake(string stateRoot, string state, int keep)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(state) || keep < 0)
            return 0;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var doomed = db.Wake.AsNoTracking()
                .Where(x => x.State == state)
                .OrderByDescending(x => x.StampedUtc)
                .Skip(keep)
                .Select(x => x.Id)
                .ToList();
            if (doomed.Count == 0)
                return 0;
            foreach (var id in doomed)
            {
                var row = db.Wake.Find(id);
                if (row is not null)
                    db.Wake.Remove(row);
            }
            db.SaveChanges();
            return doomed.Count;
        });
    }

    // ---------- P1: notification-center subscriptions ----------

    public static IReadOnlyList<CdpWakeSubscriptionEntity> LoadSubscriptions(string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.Subscriptions.AsNoTracking().OrderBy(x => x.Nick).ToList();
        });
    }

    public static bool UpsertSubscription(string stateRoot, CdpWakeSubscriptionEntity sub)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || sub is null || string.IsNullOrWhiteSpace(sub.Id) || string.IsNullOrWhiteSpace(sub.Nick))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var row = db.Subscriptions.Find(sub.Id);
            if (row is null)
            {
                db.Subscriptions.Add(sub);
            }
            else
            {
                row.Nick = sub.Nick;
                row.EventKind = sub.EventKind;
                row.TaskFilter = sub.TaskFilter;
                row.CreatedUtc = sub.CreatedUtc;
            }
            db.SaveChanges();
            return true;
        });
    }

    public static int DeleteSubscriptions(string stateRoot, string? subId, string? nick, string? eventKind)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return 0;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var subIdNorm = string.IsNullOrWhiteSpace(subId) ? null : subId.Trim();
            var nickNorm = string.IsNullOrWhiteSpace(nick) ? null : nick.Trim();
            var kindNorm = string.IsNullOrWhiteSpace(eventKind) ? null : eventKind.Trim();
            var doomed = db.Subscriptions.Where(s =>
                (subIdNorm != null && s.Id == subIdNorm)
                || (nickNorm != null
                    && s.Nick == nickNorm
                    && (kindNorm == null || s.EventKind == kindNorm)))
                .ToList();
            db.Subscriptions.RemoveRange(doomed);
            db.SaveChanges();
            return doomed.Count;
        });
    }


    // ---------- P1: ignite arms (per-seat) ----------

    public static IReadOnlyList<CdpIgniteArmEntity> LoadArms(string stateRoot, string seat)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat))
            return [];
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.Arms.AsNoTracking()
                .Where(x => x.Seat == seat)
                .OrderBy(x => x.Id)
                .ToList();
        });
    }

    /// <summary>Полная замена набора армов линии — транзакционно (аналог перезаписи ignite-arms-{seat}.json).</summary>
    public static bool ReplaceArms(string stateRoot, string seat, IEnumerable<CdpIgniteArmEntity> rows)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat) || rows is null)
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var old = db.Arms.Where(x => x.Seat == seat);
            db.Arms.RemoveRange(old);
            db.Arms.AddRange(rows);
            db.SaveChanges();
            return true;
        });
    }
    /// <summary>
    /// Транзакционная синхронизация армов линии (ADR-0219 P1): upsert строк хоста и drop
    /// идентификаторов, которые хост удалил со своей последней загрузки. Строки стора,
    /// которых хост не знал (армы сиблинг-процессов), сохраняются — merge двух писателей
    /// переживает переход на witdb (A2-фикс a88771b становится транзакцией).
    /// </summary>
    public static bool SyncArms(string stateRoot, string seat, IEnumerable<CdpIgniteArmEntity> rows, IReadOnlyCollection<string> dropIds)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat) || rows is null)
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            foreach (var id in dropIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var doomed = db.Arms.Find(seat, id);
                if (doomed is not null)
                    db.Arms.Remove(doomed);
            }
            foreach (var row in rows)
            {
                if (row is null || string.IsNullOrWhiteSpace(row.Id))
                    continue;
                var existing = db.Arms.Find(seat, row.Id);
                if (existing is null)
                {
                    db.Arms.Add(new CdpIgniteArmEntity
                    {
                        Seat = seat,
                        Id = row.Id,
                        Status = row.Status,
                        Json = row.Json,
                        StampedUtc = row.StampedUtc
                    });
                }
                else
                {
                    existing.Status = row.Status;
                    existing.Json = row.Json;
                    existing.StampedUtc = row.StampedUtc;
                }
            }
            db.SaveChanges();
            return true;
        });
    }
    // ---------- P2: latch-документы (identity/presence и будущие) ----------

    /// <summary>Прочитать latch-документ; null = нет строки. EnsureMigrated идёт первым —
    /// legacy LATEST-файл импортируется при первом обращении (см. latch-класс).</summary>
    public static string? GetLatchDoc(string stateRoot, string id)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(id))
            return null;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.LatchDocs.AsNoTracking().FirstOrDefault(x => x.Id == id)?.Json;
        });
    }

    /// <summary>Записать/обновить latch-документ транзакционно (последняя запись с меткой).</summary>
    public static bool SetLatchDoc(string stateRoot, string id, string json)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(json))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var row = db.LatchDocs.Find(id);
            if (row is null)
            {
                db.LatchDocs.Add(new CdpLatchDocEntity { Id = id, Json = json, StampedUtc = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Json = json;
                row.StampedUtc = DateTimeOffset.UtcNow;
            }
            db.SaveChanges();
            return true;
        });
    }

    // ---------- P3: pressure (stash latch-doc + append-only memo журнал) ----------

    /// <summary>Append-only memo строка (pressure_memos); повторный Id — дедуп на уровне журнала.</summary>
    public static bool AppendPressureMemo(
        string stateRoot, string seat, string id, string json, DateTimeOffset stampedUtc)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat) ||
            string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(json))
            return false;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            if (db.PressureMemos.Any(x => x.Id == id))
                return false;
            db.PressureMemos.Add(new CdpPressureMemoEntity
            {
                Id = id,
                Seat = seat,
                Json = json,
                StampedUtc = stampedUtc
            });
            db.SaveChanges();
            return true;
        });
    }

    /// <summary>Хвост журнала по сиденью — последние limit записей в хронологическом порядке.</summary>
    public static IReadOnlyList<CdpPressureMemoEntity> ListPressureMemos(string stateRoot, string seat, int limit)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat) || limit < 1)
            return Array.Empty<CdpPressureMemoEntity>();
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            var rows = db.PressureMemos.AsNoTracking()
                .Where(x => x.Seat == seat)
                .OrderByDescending(x => x.StampedUtc)
                .Take(limit)
                .ToList();
            rows.Reverse();
            return rows;
        });
    }

    /// <summary>Сколько memo строк в журнале по сиденью.</summary>
    public static int CountPressureMemos(string stateRoot, string seat)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || string.IsNullOrWhiteSpace(seat))
            return 0;
        return WithDb(stateRoot, db =>
        {
            EnsureMigratedUnlocked(db, stateRoot);
            return db.PressureMemos.AsNoTracking().Count(x => x.Seat == seat);
        });
    }

    // ---------- инфраструктура (паттерн IntercomJournalStore, ADR-0219) ----------

    static T WithDb<T>(string stateRoot, Func<CdpStateDbContext, T> action)
    {
        Directory.CreateDirectory(stateRoot);
        var path = DbPath(stateRoot);
        var attempts = 8;
        for (var i = 0; ; i++)
        {
            try
            {
                using var fileGate = new WitDbFileGate(path, FileGateWait);
                lock (Gate)
                {
                    var options = new DbContextOptionsBuilder<CdpStateDbContext>()
                        .UseWitDb($"Data Source={path}")
                        .Options;
                    using var db = new CdpStateDbContext(options);
                    db.Database.EnsureCreated();
                    return action(db);
                }
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(Math.Min(800, 60 * (i + 1)));
            }
            catch (Exception ex) when (i < attempts - 1 && IsTransientLock(ex))
            {
                Thread.Sleep(Math.Min(800, 60 * (i + 1)));
            }
        }
    }

    /// <summary>Migration-on-first-run: legacy JSON зоопарк → коллекции, файл → .migrated-{stamp}.bak (interop-only).</summary>
    static void EnsureMigratedUnlocked(CdpStateDbContext db, string stateRoot)
    {
        MigrateWakeQueue(db, stateRoot);
        MigrateArms(db, stateRoot);
        EnsureRegistrySeeded(db);
    }

    static void MigrateWakeQueue(CdpStateDbContext db, string stateRoot)
    {
        var legacy = LegacyWakeQueuePath(stateRoot);
        if (!File.Exists(legacy))
            return;
        try
        {
            WakeDispatchLegacy? legacyDoc = null;
            try
            {
                legacyDoc = JsonSerializer.Deserialize<WakeDispatchLegacy>(File.ReadAllText(legacy), LegacyOpts);
            }
            catch
            {
                /* corrupt — leave file, no import */
            }

            if (legacyDoc?.Queue is { Count: > 0 })
            {
                var existing = db.Wake.AsNoTracking().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                var batch = new List<CdpWakeEnvelopeEntity>(64);
                foreach (var q in legacyDoc.Queue)
                {
                    if (q is null || string.IsNullOrWhiteSpace(q.Id) || !existing.Add(q.Id))
                        continue;
                    batch.Add(new CdpWakeEnvelopeEntity
                    {
                        Id = q.Id,
                        Kind = q.Kind ?? "letter",
                        Nick = q.Nick ?? "",
                        Body = q.Body ?? "",
                        State = string.IsNullOrWhiteSpace(q.State) ? "pending" : q.State,
                        Seat = q.Seat,
                        Session = q.Session,
                        StampedUtc = q.StampedUtc ?? DateTimeOffset.UtcNow,
                        DeliveredUtc = q.DeliveredUtc
                    });
                    if (batch.Count >= 100)
                    {
                        db.Wake.AddRange(batch);
                        db.SaveChanges();
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                {
                    db.Wake.AddRange(batch);
                    db.SaveChanges();
                }
            }

            var qs = db.QueueState.Find("wake");
            if (qs is null)
            {
                db.QueueState.Add(new CdpQueueStateEntity
                {
                    Id = "wake",
                    Stopped = legacyDoc?.Stopped ?? false,
                    CooldownSeconds = legacyDoc?.DeliveryCooldownSeconds ?? 15,
                    StampedUtc = DateTimeOffset.UtcNow
                });
                db.SaveChanges();
            }

            RenameLegacyAside(legacy);
        }
        catch
        {
            /* leave legacy file; next call retries */
        }
    }

    static void MigrateArms(CdpStateDbContext db, string stateRoot)
    {
        var legacyFiles = Directory.EnumerateFiles(stateRoot, LegacyArmsFilePrefix + "*" + LegacyArmsFileSuffix);
        foreach (var file in legacyFiles)
        {
            try
            {
                var seat = Path.GetFileName(file)[LegacyArmsFilePrefix.Length..^LegacyArmsFileSuffix.Length];
                if (string.IsNullOrWhiteSpace(seat))
                    continue;
                var arms = JsonSerializer.Deserialize<List<ArmLegacy>>(File.ReadAllText(file), LegacyOpts);
                if (arms is null || arms.Count == 0)
                {
                    RenameLegacyAside(file);
                    continue;
                }

                db.Arms.RemoveRange(db.Arms.Where(x => x.Seat == seat));
                db.Arms.AddRange(arms.Select(a => new CdpIgniteArmEntity
                {
                    Seat = seat,
                    Id = a.Id ?? Guid.NewGuid().ToString("N"),
                    Status = a.Status ?? "armed",
                    Json = JsonSerializer.Serialize(a),
                    StampedUtc = a.CreatedUtc ?? DateTimeOffset.UtcNow
                }));
                db.SaveChanges();
                RenameLegacyAside(file);
            }
            catch
            {
                /* leave file; next call retries */
            }
        }
    }

    static void EnsureRegistrySeeded(CdpStateDbContext db)
    {
        if (db.Registry.AsNoTracking().Any())
            return;
        var now = DateTimeOffset.UtcNow;
        CdpStoreRegistryEntity Row(string name, string owner, string format, string? note = null) =>
            new() { Name = name, Owner = owner, Format = format, Note = note, StampedUtc = now };
        db.Registry.AddRange(
            Row("wake", "CideWakeDispatch (ADR-0213)", "witdb"),
            Row("arms", "IdeIgniteArmHost (ADR-0219 W1)", "witdb"),
            Row("store-registry", "CdpStateStore (ADR-0219 P0)", "witdb"),
            Row("queue-state", "CdpStateStore queue seats", "witdb"),
            Row("wake-subscriptions", "NotificationCenter (ADR-0213)", "witdb"),
            Row("latch_docs", "latch latches + pressure-stash:{seat} (P2-P3)", "witdb"),
            Row("pressure-memos", "IdePressureChannel (ADR-0219 P3)", "witdb"),
            GS1("cide-latches", "IDE interop latch zoo (~25 Cide*Latch LATEST files; identity/presence mirrored to witdb)"),
            GS1("teeth-tape", "CIDE teeth tape jsonl — wake/delivery audit, interop"),
            GS1("remount-wake", "remount-*.pending.json — service restart wake notes, interop"),
            GS1("cdb-channel", "IDE status/telemetry channel to CdpService host, interop"));
        db.SaveChanges();
    }

    static CdpStoreRegistryEntity GS1(string name, string note) =>
        new()
        {
            Name = name,
            Owner = "GS-HS1 interop channels",
            Format = "file",
            Note = note,
            StampedUtc = DateTimeOffset.UtcNow
        };

    static void RenameLegacyAside(string legacy)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var dest = legacy + $".migrated-{stamp}.bak";
        File.Move(legacy, dest, overwrite: false);
    }

    static bool IsTransientLock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
                || m.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || m.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || m.Contains("locking", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    sealed class WitDbFileGate : IDisposable
    {
        readonly Mutex _mutex;
        readonly bool _owned;

        public WitDbFileGate(string databasePath, TimeSpan wait)
        {
            var key = string.IsNullOrWhiteSpace(databasePath)
                ? "default"
                : Path.GetFullPath(databasePath).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
            _mutex = new Mutex(initiallyOwned: false, name: $@"Local\CdpMcp.WitDb.{hash}");
            try
            {
                _owned = _mutex.WaitOne(wait <= TimeSpan.Zero ? FileGateWait : wait);
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
            }

            if (!_owned)
                throw new IOException(
                    $"cdp-state.witdb busy: cannot lock {databasePath} within {wait.TotalSeconds:0}s");
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* not owner */ }
            }

            _mutex.Dispose();
        }
    }

    // ---------- legacy shapes (interop) ----------

    sealed class WakeDispatchLegacy
    {
        public bool Stopped { get; set; }
        public int DeliveryCooldownSeconds { get; set; }
        public List<WakeEnvelopeLegacy>? Queue { get; set; }
    }

    sealed class WakeEnvelopeLegacy
    {
        public string? Id { get; set; }
        public string? Kind { get; set; }
        public string? Nick { get; set; }
        public string? Body { get; set; }
        public string? State { get; set; }
        public string? Seat { get; set; }
        public string? Session { get; set; }
        public DateTimeOffset? StampedUtc { get; set; }
        public DateTimeOffset? DeliveredUtc { get; set; }
    }

    sealed class ArmLegacy
    {
        public string? Id { get; set; }
        public string? Event { get; set; }
        public string? Task { get; set; }
        public string? Status { get; set; }
        public string? Harness { get; set; }
        public string? OpencodeSession { get; set; }
        public DateTimeOffset? CreatedUtc { get; set; }
    }
}

/// <summary>
/// ADR-0219 §DI.4 (stores) — instance over the static module: one instance = one state root.
/// WitDbCdpStateStore (ICdpStateStore) and tests compose this; <see cref="CdpStateStoreDb"/>
/// stays the stateless module underneath (transitional — no new static mutations, ADR §DI.5).
/// </summary>
public sealed class CdpStateStore
{
    readonly string _stateRoot;

    public CdpStateStore(string stateRoot)
        => _stateRoot = stateRoot;

    public string StateRoot => _stateRoot;
    public string DbPath => CdpStateStoreDb.DbPath(_stateRoot);

    /// <summary>Const-переэкспорт модуля — живые вызыватели (CideWakeDispatch) читают через тип инстанса.</summary>
    public const string FileName = CdpStateStoreDb.FileName;

    // ---------- P2: latch-документы ----------

    public string? GetLatchDoc(string id) => CdpStateStoreDb.GetLatchDoc(_stateRoot, id);

    public bool SetLatchDoc(string id, string json) => CdpStateStoreDb.SetLatchDoc(_stateRoot, id, json);

    public bool RegisterStore(string name, string owner, string format, string? note = null)
        => CdpStateStoreDb.RegisterStore(_stateRoot, name, owner, format, note);

    public IReadOnlyList<CdpStoreRegistryEntity> ListStores()
        => CdpStateStoreDb.ListStores(_stateRoot);

    public bool EnqueueWake(CdpWakeEnvelopeEntity row)
        => CdpStateStoreDb.EnqueueWake(_stateRoot, row);

    public IReadOnlyList<CdpWakeEnvelopeEntity> LoadWake(string? state = null, int limit = 200)
        => CdpStateStoreDb.LoadWake(_stateRoot, state, limit);

    public bool SetWakeState(string id, string state, string? detail = null, string? skippedReason = null)
        => CdpStateStoreDb.SetWakeState(_stateRoot, id, state, detail, skippedReason);

    public bool SetWakeStopped(bool stopped)
        => CdpStateStoreDb.SetWakeStopped(_stateRoot, stopped);

    public bool IsWakeStopped()
        => CdpStateStoreDb.IsWakeStopped(_stateRoot);

    public CdpQueueStateEntity? LoadQueueState(string id)
        => CdpStateStoreDb.LoadQueueState(_stateRoot, id);

    public bool SetQueueState(CdpQueueStateEntity row)
        => CdpStateStoreDb.SetQueueState(_stateRoot, row);

    public int PurgeWake(string state, int keep)
        => CdpStateStoreDb.PurgeWake(_stateRoot, state, keep);

    public IReadOnlyList<CdpWakeSubscriptionEntity> LoadSubscriptions()
        => CdpStateStoreDb.LoadSubscriptions(_stateRoot);

    public bool UpsertSubscription(CdpWakeSubscriptionEntity sub)
        => CdpStateStoreDb.UpsertSubscription(_stateRoot, sub);

    public int DeleteSubscriptions(string? subId, string? nick, string? eventKind)
        => CdpStateStoreDb.DeleteSubscriptions(_stateRoot, subId, nick, eventKind);

    public IReadOnlyList<CdpIgniteArmEntity> LoadArms(string seat)
        => CdpStateStoreDb.LoadArms(_stateRoot, seat);

    public bool ReplaceArms(string seat, IEnumerable<CdpIgniteArmEntity> rows)
        => CdpStateStoreDb.ReplaceArms(_stateRoot, seat, rows);

    public bool SyncArms(string seat, IEnumerable<CdpIgniteArmEntity> rows, IReadOnlyCollection<string> dropIds)
        => CdpStateStoreDb.SyncArms(_stateRoot, seat, rows, dropIds);

    // ---------- P3: pressure ----------

    public bool AppendPressureMemo(string seat, string id, string json, DateTimeOffset stampedUtc)
        => CdpStateStoreDb.AppendPressureMemo(_stateRoot, seat, id, json, stampedUtc);

    public IReadOnlyList<CdpPressureMemoEntity> ListPressureMemos(string seat, int limit)
        => CdpStateStoreDb.ListPressureMemos(_stateRoot, seat, limit);

    public int CountPressureMemos(string seat)
        => CdpStateStoreDb.CountPressureMemos(_stateRoot, seat);
}
