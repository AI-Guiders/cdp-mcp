#nullable enable

namespace CdpMcp;

internal static partial class IdeBuildSaChannel
{
    static string PulseLine(Snap snap, string verdict)
    {
        var dap = snap.ActiveDap ? (snap.Stopped ? "DAP STOPPED" : "DAP active") : "dap idle";
        return $"build_desk · {verdict} · {dap} · dirty={snap.Dirty.Count} · ahead={snap.Ahead?.ToString() ?? "?"}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (snap.ScmRoot is not { Length: > 0 } && snap.Target is not { Length: > 0 })
            return ("need_more", "No project/scm — cdp_open before build/ship.");

        if (snap.ActiveDap && scope is "session" or "build")
            return ("stop_rebuild", "DAP holds PDB — debug_stop before cdp_build.");

        if (scope is "session" or "ship")
        {
            if (snap.SecretHits > 0)
                return ("preflight", "Dirty includes secret-risk paths — git_preflight before commit.");
            if (snap.Dirty.Count > 0)
                return ("ship", "Dirty tree — git_plan logical slices (standing allow push after).");
            if (snap.Ahead is > 0)
                return ("push", "Clean but ahead of upstream — git_push when ready.");
        }

        if (scope == "build" || scope == "session")
        {
            if (!snap.TargetOk)
                return ("need_more", "No build target — cdp_open / path=.");
            if (snap.ActiveDap)
                return ("stop_rebuild", "DAP holds PDB — debug_stop before cdp_build.");
            return ("build", "Ready to cdp_build (no last-build cache in v0)." );
        }

        return ("clean", "Clean tree, not ahead — nothing to ship.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();
        switch (verdict)
        {
            case "stop_rebuild":
                list.Add(new { go = "debug_desk", label = "Debug-SA", why = "fuse before stop" });
                list.Add(new { go = "debug", label = "debug_stop", why = "op=stop — release PDB" });
                list.Add(new { go = "build", label = "Rebuild", why = "cdp_build after stop" });
                list.Add(new { go = "qrh", label = "QRH dap-pdb-lock", why = "procedure" });
                break;
            case "preflight":
                list.Add(new { go = "git_scene", label = "Git scene", why = "confirm dirty" });
                list.Add(new { go = "git_draft", label = "git_preflight", why = "exclude secrets" });
                break;
            case "ship":
                list.Add(new { go = "git_draft", label = "git_plan", why = "logical commits" });
                list.Add(new { go = "ecl", label = "ECL ship", why = "checklist" });
                list.Add(new { go = "qrh", label = "QRH ship-dirty", why = "procedure" });
                break;
            case "push":
                list.Add(new { go = "git_scene", label = "Git scene", why = "ahead/behind" });
                list.Add(new { go = "ecl", label = "ECL ship push", why = "standing allow" });
                break;
            case "build":
                list.Add(new { go = "build", label = "cdp_build", why = "session project" });
                list.Add(new { go = "test_desk", label = "Test-SA", why = "after build" });
                break;
            case "clean":
                list.Add(new { go = "git_scene", label = "Git scene", why = "confirm clean" });
                break;
            default:
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA" });
        return Dedup(list);
    }

    static object[] Dedup(List<object> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outList = new List<object>();
        foreach (var item in list)
        {
            var t = item.GetType();
            var key = (t.GetProperty("label")?.GetValue(item) as string ?? "") + "\0" +
                      (t.GetProperty("why")?.GetValue(item) as string ?? "");
            if (!seen.Add(key)) continue;
            outList.Add(item);
        }

        return outList.ToArray();
    }

}
