using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using Microsoft.EntityFrameworkCore;

namespace CdpMcp.IntentWorkspace;

internal sealed class IntentWorkspaceState
{
    public Guid? ActiveIntentId { get; set; }
    public Guid? ActiveSceneId { get; set; }
    public Guid? ActiveStageId { get; set; }
    public string DatabasePath { get; set; } = "";
}

internal sealed partial class IntentWorkspaceStore(
    DbContextOptions<IntentWorkspaceDbContext> options,
    string databasePath = "")

{
    private static readonly Lock DbGate = new();
    private static readonly HashSet<string> StageStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "active", "done", "parked", "deferred"
    };

    private readonly string _databasePath = databasePath;

    private IntentWorkspaceDbContext Open() => new(options);

    /// <summary>Same-seat zombie remount safety — wait for named Mutex (default 12s).</summary>
    public static readonly TimeSpan DefaultFileGateWait = TimeSpan.FromSeconds(12);

    /// <summary>Bootstrap EnsureCreated + migrations on large DBs (same-seat only).</summary>
    public static readonly TimeSpan BootstrapFileGateWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Cross-process gate for the <em>same seat</em> WitDB file (zombie remount of one CdpMcp.exe).
    /// Dual seats use separate files under <c>StateRoot/{seat}/</c> — not this mutex.
    /// Nested Enter on the same thread is OK (Windows Mutex recursion).
    /// </summary>
    public static IDisposable EnterFileGate(string databasePath) =>
        EnterFileGate(databasePath, DefaultFileGateWait);

    public static IDisposable EnterFileGate(string databasePath, TimeSpan wait) =>
        new WitDbFileGate(databasePath, wait);

    /// <summary>Serialize WitDB access (in-proc Lock + same-seat Mutex + brief lock retry).</summary>
    private T WithDb<T>(Func<IntentWorkspaceDbContext, T> action)
    {
        using var fileGate = EnterFileGate(_databasePath);
        const int attempts = 8;
        for (var i = 0; ; i++)
        {
            try
            {
                lock (DbGate)
                {
                    using var db = Open();
                    return action(db);
                }
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(50 * (i + 1));
            }
            catch (Exception ex) when (i < attempts - 1 && IsTransientDbLock(ex))
            {
                Thread.Sleep(50 * (i + 1));
            }
        }
    }

    private void WithDb(Action<IntentWorkspaceDbContext> action)
    {
        WithDb<object?>(db =>
        {
            action(db);
            return null;
        });
    }

    static bool IsTransientDbLock(Exception ex)
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
            var waitFor = wait <= TimeSpan.Zero ? DefaultFileGateWait : wait;
            try
            {
                _owned = _mutex.WaitOne(waitFor);
            }
            catch (AbandonedMutexException)
            {
                _owned = true; // previous holder crashed — we own it
            }

            if (!_owned)
                throw new IOException(
                    $"WitDB busy (same-seat zombie?): cannot lock {databasePath} within {waitFor.TotalSeconds:0}s");
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
