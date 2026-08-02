using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace CdpMcp;

/// <summary>
/// WitDB workspace bootstrap for TLS Program — owns store/path/focus state.
/// Peel target: EnsureWorkspaceDb cluster (domain program card).
/// Per-seat file under <c>StateRoot/{seat}/intent-workspace.witdb</c> — dual seats never share FileShare.None.
/// </summary>
internal sealed class WorkspaceDbHost
{
    readonly string? _pathOverride;
    readonly SessionContext _session;
    readonly object _ensureLock = new();
    IntentWorkspaceStore? _store;
    string? _openedPath;
    string _path;

    public WorkspaceDbHost(string? pathOverride, SessionContext session)
    {
        _pathOverride = pathOverride;
        _session = session;
        _path = ResolvePath();
        State = new IntentWorkspaceState { DatabasePath = _path };
    }

    public IntentWorkspaceState State { get; }

    public IntentWorkspaceStore? Store => _store;

    public string DatabasePath => _path;

    public void Invalidate()
    {
        lock (_ensureLock)
        {
            _store = null;
            _openedPath = null;
        }
    }

    public void Ensure()
    {
        CdpClientWorkspace.EnsureSessionFallback(_session);
        lock (_ensureLock)
        {
            var path = ResolvePath();
            if (_store is not null &&
                string.Equals(_openedPath, path, StringComparison.OrdinalIgnoreCase))
                return;

            _path = path;
            State.DatabasePath = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var conn = $"Data Source={path}";
            var wsOptions = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
                .UseWitDb(conn)
                .Options;

            // One gate for EnsureCreated + Ensure* (WithDb nests Mutex on same thread).
            // Mark _openedPath before releasing gate so concurrent Ensure cannot double-open FileShare.None.
            using (IntentWorkspaceStore.EnterFileGate(path, IntentWorkspaceStore.BootstrapFileGateWait))
            {
                EnsureCreatedWithRetry(wsOptions, path);

                _store = new IntentWorkspaceStore(wsOptions, path);
                _store.EnsureOpenRecentTable();
                _store.MigrateLegacyOpenRecentJsonIfPresent();
                _store.EnsureDeskSeatsTable();
                _store.MigrateLegacyDeskSeatsJsonIfPresent();
                _store.EnsureStagePhaseAffinityColumn();
                _store.EnsureStageClockColumns();
                _store.EnsureStageProductColumn();
                _store.EnsureStageEventsTable();
                _store.EnsureStageCriteriaTable();
                _store.EnsureWorkFocusTable();
                _store.WorkFocusHydrate(State);
                _store.EnsureScriptLastRunTable();

                IdeDeskSeats.Bind(_store);
                ScriptScene.Bind(_store);
                IdeStageCycle.Bind(_store, () => State, () => CdpEnumParse.ToWire(_session.Phase));
                OpenRecentStore.Configure(new WitDbOpenRecentBackend(_store, path));
                _openedPath = path;
            }
        }
    }

    public IntentWorkspaceStore Require()
    {
        Ensure();
        return _store!;
    }

    string ResolvePath() =>
        WorkspaceDbPaths.Resolve(_pathOverride, CdpProfile.StateRoot, IdeIgniteArmHost.Seat);

    static void EnsureCreatedWithRetry(DbContextOptions<IntentWorkspaceDbContext> wsOptions, string path)
    {
        // Existing seat DB: schema patches are Ensure* migrations — skip EnsureCreated
        // so we never open FileShare.None while another same-seat process holds WithDb.
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return;

        const int attempts = 8;
        for (var i = 0; ; i++)
        {
            try
            {
                using var boot = new IntentWorkspaceDbContext(wsOptions);
                boot.Database.EnsureCreated();
                return;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(80 * (i + 1));
            }
            catch (Exception ex) when (i < attempts - 1 && IsTransientLock(ex))
            {
                Thread.Sleep(80 * (i + 1));
            }
        }
    }

    static bool IsTransientLock(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase)
                || m.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || m.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
