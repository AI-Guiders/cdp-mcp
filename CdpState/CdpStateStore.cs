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
public static class CdpStateStore
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
                qs.StampedUtc = DateTimeOffset.UtcNow;
            }
            db.SaveChanges();
            return true;
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
        db.Registry.AddRange(
            new CdpStoreRegistryEntity { Name = "wake", Owner = "CideWakeDispatch (ADR-0213)", Format = "witdb", StampedUtc = DateTimeOffset.UtcNow },
            new CdpStoreRegistryEntity { Name = "arms", Owner = "IdeIgniteArmHost", Format = "witdb", StampedUtc = DateTimeOffset.UtcNow },
            new CdpStoreRegistryEntity { Name = "store-registry", Owner = "CdpStateStore (ADR-0219 P0)", Format = "witdb", StampedUtc = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

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
