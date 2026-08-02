#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Stage-cycle event ledger — SA diagnostic pointers while wall clock is open.
/// Phase segments (phase.start / phase.complete) share the same clock gate.
/// Only binds when ActiveStage has Start and no Completed. Not a score.
/// </summary>
internal static class IdeStageCycle
{
    static IntentWorkspaceStore? _store;
    static Func<IntentWorkspaceState>? _statePeek;
    static Func<string?>? _phasePeek;

    public static void Bind(
        IntentWorkspaceStore store,
        Func<IntentWorkspaceState> statePeek,
        Func<string?>? phasePeek = null)
    {
        _store = store;
        _statePeek = statePeek;
        _phasePeek = phasePeek;
    }

    /// <summary>Bound WitDB peek for organs that need live TM pulse without full cockpit compose.</summary>
    public static bool TryWorkspace(
        out IntentWorkspaceStore store,
        out IntentWorkspaceState state,
        out string? phase)
    {
        store = null!;
        state = null!;
        phase = null;
        try
        {
            var s = _store;
            var st = _statePeek?.Invoke();
            if (s is null || st is null)
                return false;
            store = s;
            state = st;
            phase = _phasePeek?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Append to open-clock active stage. No-op if clock closed / no focus.</summary>
    public static void TryAppend(string kind, string source, string summary, string? refId = null) =>
        _ = TryAppendCore(kind, source, summary, refId);

    static bool TryAppendCore(string kind, string source, string summary, string? refId = null)
    {
        try
        {
            var store = _store;
            var state = _statePeek?.Invoke();
            if (store is null || state is null)
                return false;
            if (state.ActiveStageId is not { } sid)
                return false;
            return store.StageEventTryAppendOpenClock(sid, kind, source, summary, refId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Explicit Start Phase — wall segment begin (needs open stage clock).</summary>
    public static bool TryPhaseStart(string? phase, out string used)
    {
        used = ResolvePhase(phase);
        if (used.Length == 0)
            return false;
        return TryAppendCore("phase.start", "phase", used);
    }

    public static bool TryPhaseStart(string? phase = null) => TryPhaseStart(phase, out _);

    /// <summary>Explicit Complete Phase — wall segment end (needs open stage clock).</summary>
    public static bool TryPhaseComplete(string? phase, out string used)
    {
        used = ResolvePhase(phase);
        if (used.Length == 0)
            return false;
        return TryAppendCore("phase.complete", "phase", used);
    }

    public static bool TryPhaseComplete(string? phase = null) => TryPhaseComplete(phase, out _);

    /// <summary>
    /// Auto on session phase transition (cdp_context phase=): complete previous, start next.
    /// Same ledger gate as note/wait/fail — only while stage wall clock open.
    /// </summary>
    public static void TryPhaseTransition(string? fromPhase, string toPhase)
    {
        var to = NormalizePhase(toPhase);
        if (to.Length == 0)
            return;
        var from = NormalizePhase(fromPhase);
        if (from.Length > 0 && !from.Equals(to, StringComparison.OrdinalIgnoreCase))
            _ = TryAppendCore("phase.complete", "phase", from);
        _ = TryAppendCore("phase.start", "phase", to);
    }

    static string ResolvePhase(string? phase)
    {
        var p = NormalizePhase(phase);
        if (p.Length > 0)
            return p;
        return NormalizePhase(_phasePeek?.Invoke());
    }

    static string NormalizePhase(string? s) => (s ?? "").Trim().ToLowerInvariant();

    public static string MapIgniteError(string? err) =>
        err switch
        {
            "busy_timeout" => "ignite.busy_timeout",
            "chat_not_found" => "ignite.chat_not_found",
            _ when IdeIgniteArmHost.ShouldEnterProviderBlockedContinuity(err)
                => "ignite.provider_blocked",
            _ => "ignite.fire_fail"
        };
}
