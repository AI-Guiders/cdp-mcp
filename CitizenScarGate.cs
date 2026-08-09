#nullable enable

namespace CdpMcp;

/// <summary>
/// Blast-radius gate by HandKind under SoftFL apply arm.
/// Dig free; Mutate/Verify/Deploy need seeded SoftFlLeaf; Mutate path must match leaf.
/// Scars from <see cref="CitizenScarLedger"/> — memo alone ≠ stop the hand.
/// </summary>
internal static class CitizenScarGate
{
    public const string RefusePathMutateOffLeaf = "scar_path_mutate_off_leaf";
    public const string RefuseMutateWithoutLeaf = "scar_mutate_without_leaf";
    public const string RefuseVerifyDeployWithoutLeaf = "scar_verify_deploy_without_leaf";

    /// <summary>
    /// Soft-refuse before host execute when SoftFL apply is armed and blast radius violated.
    /// Returns null when Dig / unarmed / force= / allowed.
    /// </summary>
    public static CitizenRouteHost.Applied? TryRefuse(CitizenIntentRouter.Route route)
    {
        CitizenScarLedger.EnsureBuiltins();

        if (!CitizenSoftFlLeaf.IsApplyArmed)
            return null;
        if (HasForce(route))
            return null;

        var blast = ClassifyBlast(route);
        if (blast == BlastKind.Free)
            return null;

        if (!CitizenSoftFlLeaf.HasSeededLeaf)
        {
            if (blast == BlastKind.Mutate
                && CitizenScarLedger.IsArmed(CitizenScarLedger.ScarMutateWithoutLeaf))
            {
                return Refuse(
                    route,
                    RefuseMutateWithoutLeaf,
                    "SoftFL apply armed — Mutate needs seeded SoftFlLeaf SSOT (force=true escape)");
            }

            if (blast is BlastKind.Verify or BlastKind.Deploy
                && CitizenScarLedger.IsArmed(CitizenScarLedger.ScarVerifyDeployWithoutLeaf))
            {
                return Refuse(
                    route,
                    RefuseVerifyDeployWithoutLeaf,
                    "SoftFL apply armed — Verify/Deploy need seeded SoftFlLeaf SSOT (force=true escape)");
            }

            return null;
        }

        if (blast == BlastKind.Mutate
            && !string.IsNullOrWhiteSpace(route.Path)
            && !CitizenSoftFlLeaf.MatchesPath(route.Path)
            && CitizenScarLedger.IsArmed(CitizenScarLedger.ScarPathMutateOffLeaf))
        {
            return Refuse(
                route,
                RefusePathMutateOffLeaf,
                "SoftFL apply armed — PathMutate off SoftFlLeaf path refused (leaf="
                + Path.GetFileName(CitizenSoftFlLeaf.Current.Path)
                + "; force=true escape)");
        }

        return null;
    }

    static BlastKind ClassifyBlast(CitizenIntentRouter.Route route)
    {
        // Action-level dig ops on Buffer stay free even under SoftFL apply.
        if (route.Verb == CitizenIntentRouter.Verb.Buffer
            && IsBufferDigOp(route.Op))
            return BlastKind.Free;

        return route.Verb switch
        {
            CitizenIntentRouter.Verb.Take
                or CitizenIntentRouter.Verb.Open
                or CitizenIntentRouter.Verb.Kb
                or CitizenIntentRouter.Verb.Find
                or CitizenIntentRouter.Verb.Files
                or CitizenIntentRouter.Verb.Disk
                or CitizenIntentRouter.Verb.FindBuf
                or CitizenIntentRouter.Verb.Shell
                or CitizenIntentRouter.Verb.Man
                or CitizenIntentRouter.Verb.Health
                or CitizenIntentRouter.Verb.Session
                or CitizenIntentRouter.Verb.Context
                or CitizenIntentRouter.Verb.DialogMemory
                or CitizenIntentRouter.Verb.Inventory
                or CitizenIntentRouter.Verb.Go
                or CitizenIntentRouter.Verb.Drill
                or CitizenIntentRouter.Verb.Detail
                or CitizenIntentRouter.Verb.Intercom
                or CitizenIntentRouter.Verb.Share
                => BlastKind.Free,

            CitizenIntentRouter.Verb.Replace
                or CitizenIntentRouter.Verb.ReplaceAll
                or CitizenIntentRouter.Verb.Create
                or CitizenIntentRouter.Verb.Append
                or CitizenIntentRouter.Verb.Delete
                or CitizenIntentRouter.Verb.Edit
                or CitizenIntentRouter.Verb.Put
                or CitizenIntentRouter.Verb.Sniper
                or CitizenIntentRouter.Verb.Buffer
                => BlastKind.Mutate,

            CitizenIntentRouter.Verb.Build
                or CitizenIntentRouter.Verb.Test
                or CitizenIntentRouter.Verb.TestPlan
                or CitizenIntentRouter.Verb.TestScene
                or CitizenIntentRouter.Verb.VerifyWave
                or CitizenIntentRouter.Verb.BuildSa
                or CitizenIntentRouter.Verb.TestSa
                => BlastKind.Verify,

            CitizenIntentRouter.Verb.Deploy => BlastKind.Deploy,

            _ => BlastKind.Free
        };
    }

    static bool IsBufferDigOp(string? op)
    {
        if (string.IsNullOrWhiteSpace(op))
            return false;
        return op.Equals("read", StringComparison.OrdinalIgnoreCase)
            || op.Equals("scene", StringComparison.OrdinalIgnoreCase)
            || op.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || op.Equals("disk_peek", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasForce(CitizenIntentRouter.Route route)
    {
        if (route.Op is not null
            && route.Op.Equals("force", StringComparison.OrdinalIgnoreCase))
            return true;

        var raw = route.Raw ?? "";
        var f = CitizenIntentRouter.ExtractKeyedValue(raw, "force");
        if (string.IsNullOrWhiteSpace(f))
            return false;
        if (bool.TryParse(f, out var b))
            return b;
        return f.Equals("1", StringComparison.OrdinalIgnoreCase)
            || f.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    static CitizenRouteHost.Applied Refuse(
        CitizenIntentRouter.Route route,
        string refuseId,
        string reason)
    {
        return new CitizenRouteHost.Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: false,
            Action: "refuse",
            Path: route.Path,
            Go: route.Go,
            Cmd: route.Cmd,
            Pulse: refuseId + " · " + Trunc(reason, 160),
            Reason: refuseId + ": " + reason);
    }

    static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..(max - 1)] + "…";
    }

    enum BlastKind
    {
        Free,
        Mutate,
        Verify,
        Deploy
    }
}
