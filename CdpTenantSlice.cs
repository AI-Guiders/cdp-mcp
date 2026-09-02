#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;
using TerminalMcp.Core;

namespace CdpMcp;

/// <summary>Per-tenant isolated session, buffers, shell, WitDB, settings (ADR-0200).</summary>
internal sealed class CdpTenantSlice : IDisposable
{
    public CdpTenantKey Key { get; }
    public SessionContext Session { get; }
    public DocumentBufferStore DocStore { get; }
    public WorkspaceDbHost Workspace { get; }
    public ShellHabitat Shell { get; }
    public IdeSettingsHabitat IdeSettings { get; }
    readonly IDisposable? _diskSyncWatch;
    readonly string _tenantStateRoot;
    DateTimeOffset _lastAccessUtc = DateTimeOffset.UtcNow;

    public CdpTenantSlice(
        CdpTenantKey key,
        SessionContext session,
        DocumentBufferStore docStore,
        WorkspaceDbHost workspace,
        ShellHabitat shell,
        IdeSettingsHabitat ideSettings,
        IDisposable? diskSyncWatch,
        string tenantStateRoot)
    {
        Key = key;
        Session = session;
        DocStore = docStore;
        Workspace = workspace;
        Shell = shell;
        IdeSettings = ideSettings;
        _diskSyncWatch = diskSyncWatch;
        _tenantStateRoot = tenantStateRoot;
    }

    public DateTimeOffset LastAccessUtc => _lastAccessUtc;

    public void Touch() => _lastAccessUtc = DateTimeOffset.UtcNow;

    public IDisposable EnterScope() =>
        Key.IsLegacyDefault ? NoOpDisposable.Instance : CdpProfile.EnterTenantStateRoot(_tenantStateRoot);

    public void Dispose()
    {
        _diskSyncWatch?.Dispose();
    }

    sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
