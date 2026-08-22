#nullable enable

using CdpMcp.Habitat;

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

    static readonly Dictionary<string, NextHint[]> BuildNextRows = new(StringComparer.Ordinal)
    {
        ["stop_rebuild"] =
        [
            new("debug_desk", "Debug-SA", "fuse before stop"),
            new("debug", "debug_stop", "op=stop — release PDB"),
            new("build", "Rebuild", "cdp_build after stop"),
            new("qrh", "QRH dap-pdb-lock", "procedure"),
        ],
        ["preflight"] =
        [
            new("git_scene", "Git scene", "confirm dirty"),
            new("git_draft", "git_preflight", "exclude secrets"),
        ],
        ["ship"] =
        [
            new("git_draft", "git_plan", "logical commits"),
            new("ecl", "ECL ship", "checklist"),
            new("qrh", "QRH ship-dirty", "procedure"),
        ],
        ["push"] =
        [
            new("git_scene", "Git scene", "ahead/behind"),
            new("ecl", "ECL ship push", "standing allow"),
        ],
        ["build"] =
        [
            new("build", "cdp_build", "session project"),
            new("test_desk", "Test-SA", "after build"),
        ],
        ["clean"] = [new("git_scene", "Git scene", "confirm clean")],
    };

    static readonly NextHint[] BuildNextFallback = [new("open", "cdp_open", "root project")];
    static readonly NextHint[] BuildNextTail = [new("alert", "EICAS", "attention SA")];

    static object[] BuildNext(Snap snap, string verdict) =>
        NextHintTable.Resolve(verdict, BuildNextRows, BuildNextFallback, suffix: BuildNextTail);
}
