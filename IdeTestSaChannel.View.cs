#nullable enable

using CdpMcp.Habitat;

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

    static readonly Dictionary<string, NextHint[]> TestNextRows = new(StringComparer.Ordinal)
    {
        ["discover"] =
        [
            new("test_scene", "Discover", "cdp_test_scene — FQNs + last_run"),
            new("test", "Run", "cdp_test after discover"),
        ],
        ["retest"] =
        [
            new("test_plan", "Retest failed", "cdp_test_plan failed_first=true"),
            new("test_scene", "Scene", "inspect last_run"),
        ],
        ["green"] =
        [
            new("test_scene", "Scene", "confirm last_run"),
            new("review", "Review", "judgment after verify"),
        ],
        ["run"] = [new("test", "Run", "cdp_test")],
    };

    static readonly NextHint[] TestNextFallback =
    [
        new("open", "cdp_open", "root project"),
        new("test_scene", "Scene", "after open"),
    ];

    static readonly NextHint[] TestNextTail =
    [
        new("alert", "EICAS", "attention SA"),
        new("ecl", "ECL verify", "checklist"),
    ];

    static object[] BuildNext(Snap snap, string verdict) =>
        NextHintTable.Resolve(verdict, TestNextRows, TestNextFallback, suffix: TestNextTail);
}
