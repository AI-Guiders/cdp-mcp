#nullable enable

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

        // idle
        var launchOk = snap.LaunchPath is { Length: > 0 } && File.Exists(snap.LaunchPath);
        if (!launchOk && snap.Target is { Length: > 0 })
            return ("need_more", "No launch dll resolved — build first or check target_path.");

        if (snap.Breakpoints.Count == 0)
            return ("fix_bp", "Idle DAP — set bp before launch, or attach with process_id.");

        if (scope == "stop")
            return ("attach", "Idle — launch or attach to hit bp (attach needs process_id)." );

        return ("idle", "No active DAP — launch when ready, or attach.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();

        switch (verdict)
        {
            case "continue":
                list.Add(new { go = "debug", label = "stop_context", why = "op=stop_context — frame/locals" });
                list.Add(new { go = "debug", label = "continue", why = "op=continue" });
                list.Add(new { go = "debug", label = "step_over", why = "op=step_over" });
                break;
            case "step":
                list.Add(new { go = "debug", label = "step_over", why = "op=step_over" });
                list.Add(new { go = "debug", label = "step_into", why = "op=step_into" });
                break;
            case "stop_rebuild":
                list.Add(new { go = "debug", label = "debug_stop", why = "op=stop — release PDB" });
                list.Add(new { go = "build", label = "Rebuild", why = "after stop" });
                list.Add(new { go = "qrh", label = "QRH dap-pdb-lock", why = "procedure" });
                break;
            case "fix_bp":
                list.Add(new { go = "debug", label = "bp_add", why = "op=bp_add path+line" });
                list.Add(new { go = "debug", label = "bp_list", why = "op=bp_list" });
                break;
            case "attach":
                list.Add(new { go = "debug", label = "launch", why = "op=launch" });
                list.Add(new { go = "debug", label = "attach", why = "op=attach process_id=" });
                break;
            case "idle":
                list.Add(new { go = "debug", label = "launch", why = "op=launch" });
                list.Add(new { go = "debug", label = "scene", why = "op=scene" });
                break;
            default:
                list.Add(new { go = "debug", label = "scene", why = "op=scene" });
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA (1-bit DAP)" });
        list.Add(new { go = "ecl", label = "ECL dap-rebuild", why = "checklist" });

        if (snap.Stopped)
            list.Insert(0, new { go = "debug", label = "stop_context", why = "evidence before act" });

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<object>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key)) continue;
            deduped.Add(item);
        }

        return deduped.ToArray();
    }

}
