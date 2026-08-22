#nullable enable

namespace CdpMcp;

/// <summary>ADR-0200: stamp tenant wire on arm; enter slice on background fire (TimerLoop / lifecycle).</summary>
internal static partial class IdeIgniteArmHost
{
    static Func<CdpTenantKey, CdpTenantSlice>? TenantResolve;

    internal static void BindTenantResolver(Func<CdpTenantKey, CdpTenantSlice> resolve) =>
        TenantResolve = resolve;

    internal static void StampTenantWire(IgniteArm arm) =>
        arm.TenantWire = CdpTenantExecutionContext.CurrentSlice?.Key.Wire;

    internal static IDisposable? EnterTenantWireScope(string? tenantWire)
    {
        if (string.IsNullOrWhiteSpace(tenantWire))
            return null;
        return EnterArmTenantScope(new IgniteArm { TenantWire = tenantWire });
    }

    internal static bool ArmTenantWireEquals(IgniteArm arm, string? scopeWire) =>
        string.IsNullOrWhiteSpace(scopeWire)
            ? string.IsNullOrWhiteSpace(arm.TenantWire)
            : string.Equals(arm.TenantWire, scopeWire, StringComparison.Ordinal);

    /// <summary>Distinct tenant wires on armed last_once timer arms (legacy = null).</summary>
    internal static IReadOnlyList<string?> DistinctTenantWiresFromArmedWorkTimers()
    {
        EnsureLoaded();
        lock (Gate)
        {
            var set = new HashSet<string?>();
            foreach (var a in Arms)
            {
                if (!string.Equals(a.Status, "armed", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(a.Event, "timer", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!a.LastOnce)
                    continue;
                set.Add(string.IsNullOrWhiteSpace(a.TenantWire) ? null : a.TenantWire);
            }

            if (set.Count == 0)
                set.Add(null);
            return set.ToList();
        }
    }

    internal static IDisposable? EnterArmTenantScope(IgniteArm arm)
    {
        if (!TryResolveArmTenant(arm, out var slice))
            return null;
        return new ArmTenantScope(slice);
    }

    internal static bool TryResolveArmTenant(IgniteArm arm, out CdpTenantSlice slice)
    {
        slice = null!;
        if (TenantResolve is null || string.IsNullOrWhiteSpace(arm.TenantWire))
            return false;
        if (!TryParseTenantWire(arm.TenantWire, out var key))
            return false;
        slice = TenantResolve(key);
        return true;
    }

    static bool TryParseTenantWire(string wire, out CdpTenantKey key)
    {
        key = default;
        var parts = wire.Split(':', 3);
        if (parts.Length != 3)
            return false;
        key = new CdpTenantKey(parts[0], parts[1], parts[2]);
        return true;
    }

    sealed class ArmTenantScope(CdpTenantSlice slice) : IDisposable
    {
        readonly IDisposable _profile = slice.EnterScope();
        readonly IDisposable _exec = CdpTenantExecutionContext.Enter(slice);

        public void Dispose()
        {
            _exec.Dispose();
            _profile.Dispose();
        }
    }
}
