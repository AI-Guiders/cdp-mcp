using System.Collections.Concurrent;
using Cdp.Lsp;
using TypescriptLang;

namespace CdpMcp;

/// <summary>Per-tenant language harness isolation (ADR-0200 — no cross-tenant LSP/TS bleed).</summary>
internal static partial class IdeLanguageTools
{
    static DocumentBufferStore? ActiveDocStore =>
        CdpTenantExecutionContext.CurrentSlice?.DocStore ?? _docStore;

    static readonly ConcurrentDictionary<string, LspSessionPool> TenantLspPools = new(StringComparer.Ordinal);

    static LspSessionPool ResolveLspPool()
    {
        var wire = CdpTenantExecutionContext.CurrentSlice?.Key.Wire;
        if (wire is null)
            return LspPool;
        return TenantLspPools.GetOrAdd(wire, static _ =>
        {
            var pool = new LspSessionPool();
            pool.Configure(LspPool.Presets);
            return pool;
        });
    }

    sealed class TsTenantSlot
    {
        public TypescriptLanguageClient? Client;
        public string? OpenedRoot;
        public readonly object Gate = new();
    }

    static readonly ConcurrentDictionary<string, TsTenantSlot> TenantTs = new(StringComparer.Ordinal);

    static TsTenantSlot TsSlot() =>
        TenantTs.GetOrAdd(
            CdpTenantExecutionContext.CurrentSlice?.Key.Wire ?? "legacy",
            static _ => new TsTenantSlot());
}
