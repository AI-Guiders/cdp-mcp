#nullable enable

namespace CdpMcp;

/// <summary>ADR-0200: stamp tenant wire on arm; enter slice on background fire (TimerLoop / lifecycle).</summary>
internal static partial class IdeIgniteArmHost
{
    static Func<CdpTenantKey, CdpTenantSlice>? TenantResolve;

    internal static void BindTenantResolver(Func<CdpTenantKey, CdpTenantSlice> resolve) =>
        TenantResolve = resolve;

    internal static void StampTenantWire(IgniteArm arm)
    {
        arm.TenantWire = CdpTenantExecutionContext.CurrentSlice?.Key.Wire;
        arm.ConversationId = CdpTenantRoutingContext.CurrentConversationId;
        if (string.IsNullOrWhiteSpace(arm.Chat))
            arm.Chat = ResolveChatFromTenantLatch(arm.TenantWire, arm.ConversationId, null);
    }

    /// <summary>CDT chat= — arm field, then per-conversation latch, then bridge-wide latch.</summary>
    internal static string? ResolveChatFromTenantLatch(
        string? tenantWire,
        string? conversationId,
        string? armChat)
    {
        if (!string.IsNullOrWhiteSpace(armChat))
            return armChat.Trim();

        string? bridge = null;
        if (!string.IsNullOrWhiteSpace(tenantWire) && TryParseTenantWire(tenantWire, out var key))
            bridge = key.BridgeSession;

        if (string.IsNullOrWhiteSpace(bridge)
            && CdpTenantExecutionContext.CurrentSlice is { } slice
            && !slice.Key.IsLegacyDefault)
            bridge = slice.Key.BridgeSession;

        if (string.IsNullOrWhiteSpace(bridge))
            return null;

        var byConv = CdpTenantComposerLatch.ResolveDefaultChat(bridge, conversationId);
        if (!string.IsNullOrWhiteSpace(byConv))
            return byConv;

        return CdpTenantComposerLatch.ResolveDefaultChat(bridge, null);
    }

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
