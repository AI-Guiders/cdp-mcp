#nullable enable
using System.Collections.Concurrent;

namespace CdpMcp;

internal sealed class CdpTenantRegistry : IDisposable
{
    readonly ConcurrentDictionary<string, CdpTenantSlice> _slices = new(StringComparer.Ordinal);
    readonly CdpSharedKernel _kernel;
    readonly CdpTenantSlice _default;
    readonly Timer _evictionTimer;
    readonly TimeSpan _idleTtl;

    public CdpTenantRegistry(CdpSharedKernel kernel, CdpTenantSlice defaultSlice)
    {
        _kernel = kernel;
        _default = defaultSlice;
        _idleTtl = ResolveIdleTtl();
        _evictionTimer = new Timer(_ => EvictIdle(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public CdpSharedKernel Kernel => _kernel;

    public CdpTenantSlice Default => _default;

    public int ActiveCount => _slices.Count;

    public CdpTenantSlice Resolve(CdpTenantKey? key)
    {
        if (key is null || key.Value.IsLegacyDefault)
        {
            _default.Touch();
            return _default;
        }

        var normalized = key.Value;
        var composer = CdpTenantComposerLatch.Get(normalized.BridgeSession);
        if (!string.Equals(composer, normalized.Composer, StringComparison.Ordinal))
            normalized = normalized with { Composer = composer };

        var wire = normalized.Wire;
        var slice = _slices.GetOrAdd(wire, _ => CdpTenantSliceFactory.Create(_kernel, normalized));
        slice.Touch();
        return slice;
    }

    static TimeSpan ResolveIdleTtl()
    {
        var raw = Environment.GetEnvironmentVariable("CDP_TENANT_IDLE_TTL_MINUTES");
        if (int.TryParse(raw, out var minutes) && minutes is >= 5 and <= 24 * 60)
            return TimeSpan.FromMinutes(minutes);
        return TimeSpan.FromMinutes(45);
    }

    void EvictIdle()
    {
        var cutoff = DateTimeOffset.UtcNow - _idleTtl;
        foreach (var pair in _slices)
        {
            if (pair.Value.LastAccessUtc > cutoff)
                continue;
            if (_slices.TryRemove(pair.Key, out var removed))
                removed.Dispose();
        }
    }

    public void Dispose() => _evictionTimer.Dispose();
}
