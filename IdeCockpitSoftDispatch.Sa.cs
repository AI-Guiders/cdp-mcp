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
        var detailFull = OptString(args, "go_detail") is { } d
            && d.Equals("full", StringComparison.OrdinalIgnoreCase);
        goResult = SoftBoard(
            SoftOrganKind.Quality, session, docStore, null, null, args,
            flattenOrganArgs: true, wantFull: detailFull);
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

        goResult = SoftBoard(
            SoftOrganKind.Report, session, null, null, null, args, flattenOrganArgs: true);
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

        goResult = SoftBoard(
            SoftOrganKind.FindDesk, session, docStore, null, null, args, flattenOrganArgs: true);
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

        goResult = SoftBoard(
            SoftOrganKind.SaDesk, session, docStore, null, null, args, flattenOrganArgs: true);
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
        if (!IsSoft(goVerb, SoftOrganKind.RefactorPlan))
            return false;

        goResult = SoftBoard(
            SoftOrganKind.RefactorPlan, session, docStore, null, null, args,
            flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.RefactorPlan);
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

        goResult = SoftBoard(SoftOrganKind.DebugDesk, session, null, null, null, args);
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

        goResult = SoftBoard(SoftOrganKind.TestDesk, session, null, null, null, args);
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

        goResult = SoftBoard(SoftOrganKind.BuildDesk, session, null, null, null, args);
        PlaceSoft(ref goVerb, SoftOrganKind.BuildDesk);
        return true;
    }
}
