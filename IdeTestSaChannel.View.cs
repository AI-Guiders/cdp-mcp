#nullable enable

namespace CdpMcp;

internal static partial class IdeTestSaChannel
{
    static string PulseLine(Snap snap, string verdict)
    {
        if (snap.Last is null)
            return $"test_desk · {verdict} · no last_run";
        return $"test_desk · {verdict} · {(snap.Last.Success ? "ok" : "FAIL")} {snap.Last.Passed}/{snap.Last.Total}";
    }

    static (string Verdict, string Why) Decide(Snap snap, string scope)
    {
        if (!snap.Ok)
            return ("need_more", snap.Error ?? "No project — cdp_open or path= before tests.");

        if (snap.Last is null)
            return scope == "last"
                ? ("discover", "No last_run — open test_scene to discover FQNs, then cdp_test.")
                : ("discover", "No last_run — discover via test_scene, or run cdp_test.");

        if (snap.Last.Failed > 0 || !snap.Last.Success)
            return ("retest", "last_run has failures — prefer cdp_test_plan failed_first=true.");

        if (scope == "failed")
            return ("green", "Scoped to failed but last_run is green — nothing to retest.");

        return ("green", "last_run green — leave, or rerun via cdp_test if intent demands.");
    }

    static object[] BuildNext(Snap snap, string verdict)
    {
        var list = new List<object>();
        switch (verdict)
        {
            case "discover":
                list.Add(new { go = "test_scene", label = "Discover", why = "cdp_test_scene — FQNs + last_run" });
                list.Add(new { go = "test", label = "Run", why = "cdp_test after discover" });
                break;
            case "retest":
                list.Add(new { go = "test_plan", label = "Retest failed", why = "cdp_test_plan failed_first=true" });
                list.Add(new { go = "test_scene", label = "Scene", why = "inspect last_run" });
                break;
            case "green":
                list.Add(new { go = "test_scene", label = "Scene", why = "confirm last_run" });
                list.Add(new { go = "review", label = "Review", why = "judgment after verify" });
                break;
            case "run":
                list.Add(new { go = "test", label = "Run", why = "cdp_test" });
                break;
            default:
                list.Add(new { go = "open", label = "cdp_open", why = "root project" });
                list.Add(new { go = "test_scene", label = "Scene", why = "after open" });
                break;
        }

        list.Add(new { go = "alert", label = "EICAS", why = "attention SA" });
        list.Add(new { go = "ecl", label = "ECL verify", why = "checklist" });
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
