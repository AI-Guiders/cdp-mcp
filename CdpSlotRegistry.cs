#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace CdpMcp;

/// <summary>
/// ADR-0209 slot registry — machine-level witdb: <c>%LocalAppData%/cdp-mcp/slots.witdb</c>.
/// Service slots self-register {pid, port, sha, build_utc} and refresh last_seen every 5s;
/// the gatekeeper reads fresh rows (silence &gt; 15s = suspected dead) and healthz-probes
/// before forwarding — the probe is the final arbiter.
/// Cross-process: path-keyed Mutex (same pattern as intercom.witdb) + AbandonedMutex recover.
/// </summary>
public static class CdpSlotRegistry
{
    public const string FileName = "slots.witdb";
    public const int FirstSlotPort = 8772;
    public const int LastSlotPort = 8871;

    /// <summary>Rows older than this are suspected dead; the healthz probe is the final arbiter.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(15);

    static readonly Lock Gate = new();
    static readonly TimeSpan FileGateWait = TimeSpan.FromSeconds(12);

    public static string DbPath(string stateRoot) => Path.Combine(stateRoot, FileName);

    /// <summary>First free TCP port in the slot range (8772..8871). The gatekeeper's 8771 is never a slot.</summary>
    public static int PickFreePort()
    {
        for (var port = FirstSlotPort; port <= LastSlotPort; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                /* occupied — next */
            }
            finally
            {
                try { listener.Stop(); }
                catch { /* best effort */ }
            }
        }

        throw new IOException($"No free slot port in {FirstSlotPort}..{LastSlotPort}.");
    }

    public static bool Upsert(string stateRoot, CdpSlotRecord record)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || record is null || record.Pid <= 0 || record.Port is <= 0 or > 65535)
            return false;

        try
        {
            return WithDb(stateRoot, db =>
            {
                var entity = db.Slots.Find(record.Pid);
                if (entity is null)
                {
                    entity = new CdpSlotEntity { Pid = record.Pid };
                    db.Slots.Add(entity);
                }

                entity.Port = record.Port;
                entity.Sha = record.Sha ?? "";
                entity.BuildUtc = record.BuildUtc ?? "";
                entity.LastSeenUtc = record.LastSeenUtc == default ? DateTimeOffset.UtcNow : record.LastSeenUtc;
                db.SaveChanges();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Fresh rows (LastSeenUtc within MaxAge), newest first.</summary>
    public static IReadOnlyList<CdpSlotRecord> Fresh(string stateRoot)
    {
        if (string.IsNullOrWhiteSpace(stateRoot))
            return [];

        try
        {
            return WithDb(stateRoot, db =>
            {
                var cutoff = DateTimeOffset.UtcNow - MaxAge;
                return db.Slots.AsNoTracking()
                    .Where(x => x.LastSeenUtc >= cutoff)
                    .OrderByDescending(x => x.LastSeenUtc)
                    .AsEnumerable()
                    .Select(FromEntity)
                    .ToList();
            });
        }
        catch
        {
            return [];
        }
    }

    public static bool Remove(string stateRoot, int pid)
    {
        if (string.IsNullOrWhiteSpace(stateRoot) || pid <= 0)
            return false;

        try
        {
            return WithDb(stateRoot, db =>
            {
                var entity = db.Slots.Find(pid);
                if (entity is null)
                    return false;
                db.Slots.Remove(entity);
                db.SaveChanges();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    static T WithDb<T>(string stateRoot, Func<CdpSlotDbContext, T> action)
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
                    var options = new DbContextOptionsBuilder<CdpSlotDbContext>()
                        .UseWitDb($"Data Source={path}")
                        .Options;
                    using var db = new CdpSlotDbContext(options);
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

    static CdpSlotRecord FromEntity(CdpSlotEntity e) => new()
    {
        Pid = e.Pid,
        Port = e.Port,
        Sha = e.Sha,
        BuildUtc = e.BuildUtc,
        LastSeenUtc = e.LastSeenUtc
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
                    $"slots.witdb busy: cannot lock {databasePath} within {wait.TotalSeconds:0}s");
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

public sealed class CdpSlotRecord
{
    public int Pid { get; set; }
    public int Port { get; set; }
    public string? Sha { get; set; }
    public string? BuildUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class CdpSlotEntity
{
    public int Pid { get; set; }
    public int Port { get; set; }
    public string Sha { get; set; } = "";
    public string BuildUtc { get; set; } = "";
    public DateTimeOffset LastSeenUtc { get; set; }
}

public sealed class CdpSlotDbContext : DbContext
{
    public CdpSlotDbContext(DbContextOptions<CdpSlotDbContext> options)
        : base(options)
    {
    }

    public DbSet<CdpSlotEntity> Slots => Set<CdpSlotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<CdpSlotEntity>();
        e.ToTable("cdp_slots");
        e.HasKey(x => x.Pid);
        e.Property(x => x.Sha).HasMaxLength(64);
        e.Property(x => x.BuildUtc).HasMaxLength(64);
        e.HasIndex(x => x.LastSeenUtc);
    }
}
