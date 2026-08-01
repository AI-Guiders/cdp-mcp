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

    /// <summary>
    /// Cross-process gate — dual seats (cdp + cdp-debug) share the same client-roots WitDB file.
    /// In-proc Lock alone cannot serialize two CdpMcp.exe processes.
    /// </summary>
    public static IDisposable EnterFileGate(string databasePath) => new WitDbFileGate(databasePath);

    /// <summary>Serialize WitDB access (in-proc Lock + cross-process Mutex + brief lock retry).</summary>
    private T WithDb<T>(Func<IntentWorkspaceDbContext, T> action)
    {
        using var fileGate = EnterFileGate(_databasePath);
        const int attempts = 6;
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
                Thread.Sleep(40 * (i + 1));
            }
            catch (Exception ex) when (i < attempts - 1 && IsTransientDbLock(ex))
            {
                Thread.Sleep(40 * (i + 1));
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

        public WitDbFileGate(string databasePath)
        {
            var key = string.IsNullOrWhiteSpace(databasePath)
                ? "default"
                : Path.GetFullPath(databasePath).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
            _mutex = new Mutex(initiallyOwned: false, name: $@"Local\CdpMcp.WitDb.{hash}");
            try
            {
                _owned = _mutex.WaitOne(TimeSpan.FromSeconds(12));
            }
            catch (AbandonedMutexException)
            {
                _owned = true; // previous holder crashed — we own it
            }

            if (!_owned)
                throw new IOException(
                    $"WitDB busy (dual-seat?): cannot lock {databasePath} within 12s");
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
