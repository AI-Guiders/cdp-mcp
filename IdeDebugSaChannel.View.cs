#nullable enable

using CdpMcp.Habitat;

namespace CdpMcp;

internal static partial class IdeDebugSaChannel
{
    static string PulseLine(Snap snap, string verdict)
    {
        if (!snap.ActiveDap && !snap.Stopped)
            return $"debug_desk · {verdict} · idle · bp={snap.Breakpoints.Count}";
        if (snap.Stopped)
            return $"debug_desk · {verdict} · STOPPED t={snap.LastStoppedThreadId} · bp={snap.Breakpoints.Count}";
        return $"debug_desk · {verdict} · dap active · bp={snap.Breakpoints.Count}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (snap.Workspace is not { Length: > 0 })
            return ("need_more", "No project — cdp_open or workspace_path before DAP.");

        if (snap.Stopped)
        {
            if (snap.LastException is { Length: > 0 })
                return ("step", "Stopped with exception — stop_context then step; avoid blind continue.");
            if (scope == "bp" && snap.Breakpoints.Count == 0)
                return ("fix_bp", "Stopped but no stored breakpoints for target — verify bp bind.");
            return ("continue", "DAP stopped — continue or step; prefer stop_context if locus unclear.");
        }

        if (snap.ActiveDap)
            return ("stop_rebuild", "DAP active (not stopped) — debug_stop before rebuild (PDB lock).");

        var launchOk = snap.LaunchPath is { Length: > 0 } && File.Exists(snap.LaunchPath);
        if (!launchOk && snap.Target is { Length: > 0 })
            return ("need_more", "No launch dll resolved — build first or check target_path.");

        if (snap.Breakpoints.Count == 0)
            return ("fix_bp", "Idle DAP — set bp before launch, or attach with process_id.");

        if (scope == "stop")
            return ("attach", "Idle — launch or attach to hit bp (attach needs process_id)." );

        return ("idle", "No active DAP — launch when ready, or attach.");
    }

    static readonly Dictionary<string, NextHint[]> DebugNextRows = new(StringComparer.Ordinal)
    {
        ["continue"] =
        [
            new("debug", "stop_context", "op=stop_context — frame/locals"),
            new("debug", "continue", "op=continue"),
            new("debug", "step_over", "op=step_over"),
        ],
        ["step"] =
        [
            new("debug", "step_over", "op=step_over"),
            new("debug", "step_into", "op=step_into"),
        ],
        ["stop_rebuild"] =
        [
            new("debug", "debug_stop", "op=stop — release PDB"),
            new("build", "Rebuild", "after stop"),
            new("qrh", "QRH dap-pdb-lock", "procedure"),
        ],
        ["fix_bp"] =
        [
            new("debug", "bp_add", "op=bp_add path+line"),
            new("debug", "bp_list", "op=bp_list"),
        ],
        ["attach"] =
        [
            new("debug", "launch", "op=launch"),
            new("debug", "attach", "op=attach process_id="),
        ],
        ["idle"] =
        [
            new("debug", "launch", "op=launch"),
            new("debug", "scene", "op=scene"),
        ],
    };

    static readonly NextHint[] DebugNextFallback =
    [
        new("debug", "scene", "op=scene"),
        new("open", "cdp_open", "root project"),
    ];

    static readonly NextHint[] DebugNextTail =
    [
        new("alert", "EICAS", "attention SA (1-bit DAP)"),
        new("ecl", "ECL dap-rebuild", "checklist"),
    ];

    static readonly NextHint[] DebugStoppedPrefix = [new("debug", "stop_context", "evidence before act")];

    static object[] BuildNext(Snap snap, string verdict) =>
        NextHintTable.Resolve(
            verdict,
            DebugNextRows,
            DebugNextFallback,
            prefix: snap.Stopped ? DebugStoppedPrefix : default,
            suffix: DebugNextTail);
}
