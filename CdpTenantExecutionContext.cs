#nullable enable

namespace CdpMcp;

internal static class CdpTenantExecutionContext
{
    static readonly AsyncLocal<CdpTenantSlice?> Current = new();

    public static CdpTenantSlice? CurrentSlice => Current.Value;

    public static IDisposable Enter(CdpTenantSlice slice)
    {
        var prior = Current.Value;
        Current.Value = slice;
        return new Scope(prior);
    }

    sealed class Scope(CdpTenantSlice? prior) : IDisposable
    {
        public void Dispose() => Current.Value = prior;
    }
}
