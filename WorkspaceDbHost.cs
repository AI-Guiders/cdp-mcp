using Cdp.Core;
using Cdp.ScriptableIde;
using CdpMcp.IntentWorkspace;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace CdpMcp;

/// <summary>
/// WitDB workspace bootstrap for TLS Program — owns store/path/focus state.
/// Peel target: EnsureWorkspaceDb cluster (domain program card).
/// </summary>
internal sealed class WorkspaceDbHost(string? pathOverride, SessionContext session)
{
    IntentWorkspaceStore? _store;
    string? _openedPath;
    string _path = pathOverride
        ?? Path.Combine(CdpProfile.StateRoot, "intent-workspace.witdb");

    public IntentWorkspaceState State { get; } = new()
    {
        DatabasePath = pathOverride
            ?? Path.Combine(CdpProfile.StateRoot, "intent-workspace.witdb")
    };

    public IntentWorkspaceStore? Store => _store;

    public string DatabasePath => _path;

    public void Invalidate()
    {
        _store = null;
        _openedPath = null;
    }

    public void Ensure()
    {
        CdpClientWorkspace.EnsureSessionFallback(session);
        var path = pathOverride
            ?? Path.Combine(CdpProfile.StateRoot, "intent-workspace.witdb");
        if (_store is not null &&
            string.Equals(_openedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _path = path;
        State.DatabasePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var wsOptions = new DbContextOptionsBuilder<IntentWorkspaceDbContext>()
            .UseWitDb($"Data Source={path}")
            .Options;
        using (var bootGate = IntentWorkspaceStore.EnterFileGate(path))
        using (var boot = new IntentWorkspaceDbContext(wsOptions))
            boot.Database.EnsureCreated();
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
        IdeStageCycle.Bind(_store, () => State, () => CdpEnumParse.ToWire(session.Phase));
        OpenRecentStore.Configure(new WitDbOpenRecentBackend(_store, path));
        _openedPath = path;
    }

    public IntentWorkspaceStore Require()
    {
        Ensure();
        return _store!;
    }
}
