#nullable enable
using System.Collections.Concurrent;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal sealed class CdpTenantRegistry
{
    readonly ConcurrentDictionary<string, CdpTenantSlice> _slices = new(StringComparer.Ordinal);
    readonly string? _witDbPathOverride;
    readonly CdpTenantSlice _default;

    public CdpTenantRegistry(CdpSettings settings, CdpTenantSlice defaultSlice)
    {
        _witDbPathOverride = settings.IntentWorkspace.DatabasePath;
        _default = defaultSlice;
    }

    public CdpTenantSlice Default => _default;

    public int ActiveCount => _slices.Count;

    public CdpTenantSlice Resolve(CdpTenantKey? key)
    {
        if (key is null || key.Value.IsLegacyDefault)
            return _default;

        var wire = key.Value.Wire;
        return _slices.GetOrAdd(wire, _ => CreateSlice(key.Value));
    }

    CdpTenantSlice CreateSlice(CdpTenantKey key)
    {
        var tenantRoot = key.ResolveTenantStateRoot();
        var session = new SessionContext();
        var docStore = new DocumentBufferStore();
        var workspace = new WorkspaceDbHost(_witDbPathOverride, session);
        return new CdpTenantSlice(key, session, docStore, workspace, tenantRoot);
    }
}
