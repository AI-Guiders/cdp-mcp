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
        if (!IsSoft(goVerb, SoftInstrumentKind.Quality))
            return false;

        mfd = "gates";
        var detailFull = OptString(args, "go_detail") is { } d
            && d.Equals("full", StringComparison.OrdinalIgnoreCase);
        goResult = SoftBoard(
            SoftInstrumentKind.Quality, session, docStore, null, null, args,
            flattenOrganArgs: true, wantFull: detailFull);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Quality);
        return true;
    }


    static bool TryDispatchReport(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Report))
            return false;

        goResult = SoftBoard(
            SoftInstrumentKind.Report, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Report);
        return true;
    }

    static bool TryDispatchFind(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.FindDesk))
            return false;

        goResult = SoftBoard(
            SoftInstrumentKind.FindDesk, session, docStore, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.FindDesk);
        return true;
    }

    static bool TryDispatchCodeSa(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.SaDesk))
            return false;

        // SoftFL: cockpit go_detail ≠ IdeSaChannel depth; empty TileArgs used to default slim→RunGates hang.
        // Map go_detail→depth and force pulse when neither set (desk refresh must stay cheap).
        var saArgs = EnsureSaDeskDepth(args);
        goResult = SoftBoard(
            SoftInstrumentKind.SaDesk, session, docStore, null, null, saArgs, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.SaDesk);
        return true;
    }

        static bool TryDispatchRefactorPlan(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.RefactorPlan))
            return false;

        goResult = SoftBoard(
            SoftInstrumentKind.RefactorPlan, session, docStore, null, null, args,
            flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.RefactorPlan);
        return true;
    }


    static bool TryDispatchDebugSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.DebugDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.DebugDesk, session, null, null, null, args);
        PlaceSoft(ref goVerb, SoftInstrumentKind.DebugDesk);
        return true;
    }

    static bool TryDispatchTestSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.TestDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.TestDesk, session, null, null, null, args);
        PlaceSoft(ref goVerb, SoftInstrumentKind.TestDesk);
        return true;
    }

    static bool TryDispatchBuildSa(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.BuildDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.BuildDesk, session, null, null, null, args);
        PlaceSoft(ref goVerb, SoftInstrumentKind.BuildDesk);
        return true;
    }
}
