#nullable enable
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

/// <summary>
/// Stage-cycle event ledger — SA diagnostic pointers while wall clock is open.
/// Only binds when ActiveStage has Start and no Completed. Not a score.
/// </summary>
internal static class IdeStageCycle
{
    static IntentWorkspaceStore? _store;
    static Func<IntentWorkspaceState>? _statePeek;

    public static void Bind(IntentWorkspaceStore store, Func<IntentWorkspaceState> statePeek)
    {
        _store = store;
        _statePeek = statePeek;
    }

    /// <summary>Append to open-clock active stage. No-op if clock closed / no focus.</summary>
    public static void TryAppend(string kind, string source, string summary, string? refId = null)
    {
        try
        {
            var store = _store;
            var state = _statePeek?.Invoke();
            if (store is null || state is null)
                return;
            if (state.ActiveStageId is not { } sid)
                return;
            store.StageEventTryAppendOpenClock(sid, kind, source, summary, refId);
        }
        catch
        {
            // diagnostic only — never break fire/shell
        }
    }

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
