#nullable enable
using Cdp.Core;

namespace CdpMcp;

internal static partial class IdeReviewChannel
{
    static object[] BuildNext(Snap snap, Inputs i, bool eclHot)
    {
        var list = new List<object>();
        void Add(string id, string go, string label, string why)
        {
            if (list.Count >= 6) return;
            list.Add(new { id, go, label, why });
        }

        if (i.Session.Phase is not CdpPhase.Review)
            Add("n-phase", "plan", "Set phase=review", "cdp_context phase=review (session SSOT)");
        if (eclHot || i.Session.Phase is CdpPhase.Review)
            Add("n-ecl", "ecl", "ECL review", i.Ecl?.Pulse ?? "phase:review checklist");
        if (!snap.MachineOk)
            Add("n-problems", "problems", "Machine lane", "Close errors before judgment");
        if (snap.Files.Count > 0)
            Add("n-files", "review", "File cards", $"op=files · {snap.FileCount}");
        Add("n-qrh", "qrh", "eQRH skip-review", "qrh open skip-review");
        if (i.GitDirty)
            Add("n-git", "git_draft", "Commit slices", "After judgment — logical commits");
        else
            Add("n-ship", "ecl", "Ship checklist", "go=ecl when ready to handoff");
        return list.ToArray();
    }

    static object Lane(string id, bool ok, string pulse, string go) =>
        new { id, ok, pulse, go };

    static object FileDto(FileCard f) => new
    {
        path = f.Path,
        status = f.Status,
        risk = f.Risk,
        why = f.Why,
        go = f.Go
    };

}
