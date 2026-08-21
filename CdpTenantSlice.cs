#nullable enable
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

/// <summary>Per-tenant isolated session, buffers, WitDB (ADR-0200).</summary>
internal sealed class CdpTenantSlice
{
    public CdpTenantKey Key { get; }
    public SessionContext Session { get; }
    public DocumentBufferStore DocStore { get; }
    public WorkspaceDbHost Workspace { get; }
    readonly string _tenantStateRoot;

    public CdpTenantSlice(
        CdpTenantKey key,
        SessionContext session,
        DocumentBufferStore docStore,
        WorkspaceDbHost workspace,
        string tenantStateRoot)
    {
        Key = key;
        Session = session;
        DocStore = docStore;
        Workspace = workspace;
        _tenantStateRoot = tenantStateRoot;
    }

    public IDisposable EnterScope() =>
        Key.IsLegacyDefault ? NoOpDisposable.Instance : CdpProfile.EnterTenantStateRoot(_tenantStateRoot);

    sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
