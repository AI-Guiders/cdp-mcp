#nullable enable
using System.Text.Json;
using Cdp.Core;

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
        if (!IsGo(goVerb, "quality", "gates"))
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
        PlaceAndClear(ref goVerb, "quality");
        return true;
    }

    static bool TryDispatchReport(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "report", "evidence", "pfd"))
            return false;

        goResult = IdeReportBoard.Handle(session, OrganArgs(args));
        PlaceAndClear(ref goVerb, "report");
        return true;
    }

    static bool TryDispatchFind(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "find_desk", "search_desk", "code_search"))
            return false;

        goResult = IdeFindChannel.Handle(docStore, session, OrganArgs(args));
        PlaceAndClear(ref goVerb, "find_desk");
        return true;
    }

    static bool TryDispatchCodeSa(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "sa_desk", "code_sa", "pre_sa", "sa_code"))
            return false;

        goResult = IdeSaChannel.Handle(docStore, session, OrganArgs(args));
        PlaceAndClear(ref goVerb, "sa_desk");
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
        if (!IsGo(goVerb, "debug_desk", "dap_sa", "debug_sa"))
            return false;

        goResult = IdeDebugSaChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "debug_desk");
        return true;
    }

    static bool TryDispatchTestSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "test_desk", "test_sa"))
            return false;

        goResult = IdeTestSaChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "test_desk");
        return true;
    }

    static bool TryDispatchBuildSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "build_desk", "ship_desk", "build_sa", "ship_sa"))
            return false;

        goResult = IdeBuildSaChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "build_desk");
        return true;
    }
}
