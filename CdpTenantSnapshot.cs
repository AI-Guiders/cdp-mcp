#nullable enable

namespace CdpMcp;

/// <summary>ADR-0200 tenant row for /healthz diagnostics (live multiplex).</summary>
internal readonly record struct CdpTenantSnapshot(
    string Wire,
    string BridgeSession,
    string WorkspaceKey,
    string Composer,
    DateTimeOffset LastTouchUtc,
    string? ProjectRoot);
