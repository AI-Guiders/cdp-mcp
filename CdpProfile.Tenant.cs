#nullable enable

namespace CdpMcp;

partial class CdpProfile
{
    static readonly AsyncLocal<string?> TenantStateRootOverride = new();

    internal static bool HasTenantOverride => TenantStateRootOverride.Value is { Length: > 0 };

    internal static IDisposable EnterTenantStateRoot(string tenantStateRoot)
    {
        var prior = TenantStateRootOverride.Value;
        TenantStateRootOverride.Value = tenantStateRoot;
        try { Directory.CreateDirectory(tenantStateRoot); } catch { /* best-effort */ }
        return new TenantScope(prior);
    }

    sealed class TenantScope(string? prior) : IDisposable
    {
        public void Dispose() => TenantStateRootOverride.Value = prior;
    }
}
