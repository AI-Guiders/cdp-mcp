#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;

namespace CdpMcp;

internal static partial class IdeCockpitSoftDispatch
{
    static bool TryDispatchQuality(
        ref string? goVerb,
        ref object? goResult,
        ref string mfd,
        SessionContext session,
        DocumentBufferStore docStore,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.Quality))
            return false;

        mfd = "gates";
        var path = OptString(args, "path");
        if (args.TryGetValue("go_args", out var ga) && ga.ValueKind == JsonValueKind.Object
            && ga.TryGetProperty("path", out var gp) && gp.ValueKind == JsonValueKind.String)
            path ??= gp.GetString();
        var q = string.IsNullOrWhiteSpace(path)
            ? QualityGates.EvaluateStore(docStore, session.ProjectRoot)
            : QualityGates.EvaluatePath(docStore, session.ProjectRoot, path!);
        goResult = new
        {
            ok = true,
            go = "quality",
            tool = "quality_gates",
            detail = "full",
            truncated = false,
            result = q
        };
        PlaceSoft(ref goVerb, SoftOrganKind.Quality);
        return true;
    }

    static bool TryDispatchReport(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.Report))
            return false;

        goResult = IdeReportBoard.Handle(session, OrganArgs(args));
        PlaceSoft(ref goVerb, SoftOrganKind.Report);
        return true;
    }

    static bool TryDispatchFind(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.FindDesk))
            return false;

        goResult = IdeFindChannel.Handle(docStore, session, OrganArgs(args));
        PlaceSoft(ref goVerb, SoftOrganKind.FindDesk);
        return true;
    }

    static bool TryDispatchCodeSa(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.SaDesk))
            return false;

        goResult = IdeSaChannel.Handle(docStore, session, OrganArgs(args));
        PlaceSoft(ref goVerb, SoftOrganKind.SaDesk);
        return true;
    }

    static bool TryDispatchRefactorPlan(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "refactor_plan", "refactor", "cdp_refactor", "debt_scene"))
            return false;

        goResult = IdeRefactorPlanChannel.Handle(docStore, session, OrganArgs(args));
        PlaceAndClear(ref goVerb, "refactor_plan");
        return true;
    }

    static bool TryDispatchDebugSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.DebugDesk))
            return false;

        goResult = IdeDebugSaChannel.Handle(session, args);
        PlaceSoft(ref goVerb, SoftOrganKind.DebugDesk);
        return true;
    }

    static bool TryDispatchTestSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.TestDesk))
            return false;

        goResult = IdeTestSaChannel.Handle(session, args);
        PlaceSoft(ref goVerb, SoftOrganKind.TestDesk);
        return true;
    }

    static bool TryDispatchBuildSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.BuildDesk))
            return false;

        goResult = IdeBuildSaChannel.Handle(session, args);
        PlaceSoft(ref goVerb, SoftOrganKind.BuildDesk);
        return true;
    }
}
