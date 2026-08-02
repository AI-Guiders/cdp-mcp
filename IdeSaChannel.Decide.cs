#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Decide / locus / arg helpers for go=sa_desk.</summary>
internal static partial class IdeSaChannel
{
    static (string Verdict, string Why) Decide(
        GatesSnap gates,
        IdeReviewChannel.FileCard? dirty,
        ClonesSnap? clones)
    {
        var sizeDebt = gates.Findings.Any(f =>
            (f.Id is "file_lines" or "method_lines") && (f.Severity is "fail" or "warn"));
        var hardFail = gates.Fail > 0;
        var cloneHit = clones is { Ok: true, Groups: > 0 };

        if (dirty is { Risk: "secret" })
            return ("need_more", "Dirty path looks secret-sensitive — exclude before refactor ship.");

        if (hardFail && sizeDebt)
            return ("split", "Quality fail on size/method length — extract before more growth.");

        if (sizeDebt && cloneHit)
            return ("split", "Size debt + clone groups — extract shared locus first.");

        if (sizeDebt)
            return ("touch", "Size warn — prefer extract/sniper, not drive-by rewrite.");

        if (cloneHit)
            return ("touch", "Clones present — check correspondence before duplicate edits.");

        if (dirty is { Risk: "high" })
            return ("need_more", "High-risk dirty file — review blast before structural change.");

        if (gates.Warn > 0)
            return ("touch", "Soft quality warns — small moves ok.");

        return ("leave", "No strong refactor signal from gates/dirty/clones.");
    }

}
