#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace Cdp.IntercomJournal;

/// <summary>
/// Shared Radio journal SSOT — <c>%LocalAppData%/cdp-mcp/intercom.witdb</c>.
/// Not TM <c>intent-workspace.witdb</c>; not IntercomService team transport DB.
/// Cross-process: path-keyed Mutex (same pattern as TM WitDbFileGate) + AbandonedMutex recover.
/// </summary>
public static class IntercomJournalStore
{
    public const string FileName = "intercom.witdb";
    public const string LegacyJsonlFileName = "intercom-journal.jsonl";

    static readonly Lock Gate = new();
    static readonly TimeSpan FileGateWait = TimeSpan.FromSeconds(12);
    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string DbPath(string stateRoot) =>
        Path.Combine(stateRoot, FileName);

    public static string LegacyJsonlPath(string stateRoot) =>
        Path.Combine(stateRoot, LegacyJsonlFileName);

    public static bool TryAppend(string stateRoot, IntercomJournalRow row)
    {
        if (string.IsNullOrWhiteSpace(stateRoot)
            || row is null
            || string.IsNullOrWhiteSpace(row.Id)
            || string.IsNullOrWhiteSpace(row.Body))
            return false;

        try
        {
            return WithDb(stateRoot, db =>
            {
                EnsureMigratedUnlocked(db, stateRoot);
                if (db.Entries.AsNoTracking().Any(x => x.Id == row.Id))
                    return true;

                db.Entries.Add(ToEntity(row));
                db.SaveChanges();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<IntercomJournalRow> LoadTail(string stateRoot, int limit = 40)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];

        try
        {
            return WithDb(stateRoot, db =>
            {
                EnsureMigratedUnlocked(db, stateRoot);
                return db.Entries.AsNoTracking()
                    .OrderByDescending(x => x.StampedUtc)
                    .Take(limit)
                    .AsEnumerable()
                    .Reverse()
                    .Select(FromEntity)
                    .ToList();
            });
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Body/name contains (OrdinalIgnoreCase). Empty query → <see cref="LoadTail"/>.</summary>
    public static IReadOnlyList<IntercomJournalRow> SearchContains(string stateRoot, string? query, int limit = 40)
    {
        if (string.IsNullOrWhiteSpace(query))
            return LoadTail(stateRoot, limit);

        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];

        var needle = query.Trim();
        try
        {
            return WithDb(stateRoot, db =>
            {
                EnsureMigratedUnlocked(db, stateRoot);
                return db.Entries.AsNoTracking()
                    .OrderByDescending(x => x.StampedUtc)
                    .AsEnumerable()
                    .Where(x =>
                        (!string.IsNullOrEmpty(x.Body) && x.Body.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(x.Name) && x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                    .Take(limit)
                    .Reverse()
                    .Select(FromEntity)
                    .ToList();
            });
        }
        catch
        {
            return [];
        }
    }

    public static int Count(string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return 0;
        try
        {
            return WithDb(stateRoot, db =>
            {
                EnsureMigratedUnlocked(db, stateRoot);
                return db.Entries.Count();
            });
        }
        catch
        {
            return 0;
        }
    }

    static T WithDb<T>(string stateRoot, Func<IntercomJournalDbContext, T> action)
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
                    var options = new DbContextOptionsBuilder<IntercomJournalDbContext>()
                        .UseWitDb($"Data Source={path}")
                        .Options;
                    using var db = new IntercomJournalDbContext(options);
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

    /// <summary>Import legacy jsonl once under gate, then rename aside.</summary>
    static void EnsureMigratedUnlocked(IntercomJournalDbContext db, string stateRoot)
    {
        var legacy = LegacyJsonlPath(stateRoot);
        if (!File.Exists(legacy))
            return;

        try
        {
            var existing = db.Entries.AsNoTracking()
                .Select(x => x.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var batch = new List<IntercomJournalEntity>(256);
            foreach (var line in File.ReadLines(legacy))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                IntercomJournalRow? row = null;
                try
                {
                    row = JsonSerializer.Deserialize<IntercomJournalRow>(line, ReadOpts);
                }
                catch
                {
                    /* skip corrupt */
                }

                if (row is null || string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Body))
                    continue;
                if (!existing.Add(row.Id.Trim()))
                    continue;

                batch.Add(ToEntity(row));
                if (batch.Count >= 200)
                {
                    db.Entries.AddRange(batch);
                    db.SaveChanges();
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                db.Entries.AddRange(batch);
                db.SaveChanges();
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var dest = legacy + $".migrated-{stamp}.bak";
            File.Move(legacy, dest, overwrite: false);
        }
        catch
        {
            /* leave jsonl; next call retries */
        }
    }

    static IntercomJournalEntity ToEntity(IntercomJournalRow row) => new()
    {
        Id = row.Id.Trim(),
        FromSeat = row.FromSeat?.Trim() ?? "",
        ToSeat = row.ToSeat?.Trim() ?? "",
        Body = row.Body,
        Origin = row.Origin?.Trim() ?? "",
        Name = string.IsNullOrWhiteSpace(row.Name) ? null : row.Name.Trim(),
        Kind = string.IsNullOrWhiteSpace(row.Kind) ? null : row.Kind.Trim(),
        Channel = string.IsNullOrWhiteSpace(row.Channel) ? null : row.Channel.Trim(),
        StampedUtc = row.StampedUtc == default ? DateTimeOffset.UtcNow : row.StampedUtc,
        Acked = row.Acked
    };

    static IntercomJournalRow FromEntity(IntercomJournalEntity e) => new()
    {
        Id = e.Id,
        FromSeat = e.FromSeat,
        ToSeat = e.ToSeat,
        Body = e.Body,
        Origin = e.Origin,
        Name = e.Name,
        Kind = e.Kind,
        Channel = e.Channel,
        StampedUtc = e.StampedUtc,
        Acked = e.Acked
    };

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
                    $"intercom.witdb busy: cannot lock {databasePath} within {wait.TotalSeconds:0}s");
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
}
