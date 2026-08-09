#nullable enable

namespace CdpMcp;

/// <summary>
/// Host progress kind after SoftFL hands — dig ≠ mutate ≠ SoftFL done.
/// Classify from Applied.Verb/Action (Go alone is ambiguous: take+replace both editor_scene).
/// </summary>
internal enum CitizenHandKind
{
    Unknown = 0,
    Dig,
    Mutate,
    Verify,
    Radio
}

internal static class CitizenHandKindClassifier
{
    /// <summary>Mutate wins over Verify over Dig over Radio — SoftFL progress axis.</summary>
    public static CitizenHandKind Dominant(IReadOnlyList<CitizenRouteHost.Applied>? executed)
    {
        if (executed is null || executed.Count == 0)
            return CitizenHandKind.Unknown;

        var best = CitizenHandKind.Unknown;
        foreach (var a in executed)
        {
            if (!a.Ok)
                continue;
            var k = Classify(a);
            if (Rank(k) > Rank(best))
                best = k;
        }

        return best;
    }

    public static CitizenHandKind Classify(CitizenRouteHost.Applied a)
    {
        if (!string.IsNullOrWhiteSpace(a.Action)
            && a.Action.Equals("take", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Dig;

        if (Enum.TryParse<CitizenIntentRouter.Verb>(a.Verb, ignoreCase: true, out var verb))
            return ClassifyVerb(verb, a.Action);

        return ClassifyAction(a.Action);
    }

    static CitizenHandKind ClassifyVerb(CitizenIntentRouter.Verb verb, string? action) =>
        verb switch
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
                => CitizenHandKind.Dig,

            CitizenIntentRouter.Verb.Replace
                or CitizenIntentRouter.Verb.ReplaceAll
                or CitizenIntentRouter.Verb.Create
                or CitizenIntentRouter.Verb.Append
                or CitizenIntentRouter.Verb.Delete
                or CitizenIntentRouter.Verb.Edit
                or CitizenIntentRouter.Verb.Put
                or CitizenIntentRouter.Verb.Sniper
                or CitizenIntentRouter.Verb.Buffer
                => IsBufferDigOp(action) ? CitizenHandKind.Dig : CitizenHandKind.Mutate,

            CitizenIntentRouter.Verb.Build
                or CitizenIntentRouter.Verb.Test
                or CitizenIntentRouter.Verb.TestPlan
                or CitizenIntentRouter.Verb.TestScene
                or CitizenIntentRouter.Verb.VerifyWave
                or CitizenIntentRouter.Verb.BuildSa
                or CitizenIntentRouter.Verb.TestSa
                => CitizenHandKind.Verify,

            CitizenIntentRouter.Verb.Intercom
                or CitizenIntentRouter.Verb.Share
                => CitizenHandKind.Radio,

            CitizenIntentRouter.Verb.Git => ClassifyGitAction(action),

            _ => ClassifyAction(action)
        };

    static bool IsBufferDigOp(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;
        return action.Equals("read", StringComparison.OrdinalIgnoreCase)
            || action.Equals("scene", StringComparison.OrdinalIgnoreCase)
            || action.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || action.Equals("disk_peek", StringComparison.OrdinalIgnoreCase);
    }

    static CitizenHandKind ClassifyGitAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return CitizenHandKind.Dig;
        if (action.Contains("commit", StringComparison.OrdinalIgnoreCase)
            || action.Contains("push", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Mutate;
        return CitizenHandKind.Dig;
    }

    static CitizenHandKind ClassifyAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return CitizenHandKind.Unknown;
        if (action.Equals("take", StringComparison.OrdinalIgnoreCase)
            || action.Equals("read", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Dig;
        if (action.Equals("replace", StringComparison.OrdinalIgnoreCase)
            || action.Equals("edit", StringComparison.OrdinalIgnoreCase)
            || action.Equals("create", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Mutate;
        if (action.Equals("build", StringComparison.OrdinalIgnoreCase)
            || action.Equals("test", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Verify;
        if (action.Equals("intercom", StringComparison.OrdinalIgnoreCase)
            || action.Equals("radio", StringComparison.OrdinalIgnoreCase))
            return CitizenHandKind.Radio;
        return CitizenHandKind.Unknown;
    }

    static int Rank(CitizenHandKind k) => k switch
    {
        CitizenHandKind.Mutate => 4,
        CitizenHandKind.Verify => 3,
        CitizenHandKind.Dig => 2,
        CitizenHandKind.Radio => 1,
        _ => 0
    };
}
