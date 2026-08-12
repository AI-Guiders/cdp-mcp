#nullable enable
using System.Text.Json;
using Cdp.Core;
using CdpMcp.Cockpit.Surface;
using CdpMcp.IntentWorkspace;

namespace CdpMcp;

internal static partial class IdeCockpitSoftDispatch
{
    static bool TryDispatchCrm(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Crm))
            return false;

        goResult = SoftBoard(
            SoftInstrumentKind.Crm, session, null, workspaceStore, workspaceState, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Crm);
        return true;
    }

    static bool TryDispatchPlan(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Plan))
            return false;

        // Parity with Crm/Ignite/… — without flatten, go_args.tm_op|op mute (board pulse only).
        goResult = SoftBoard(
            SoftInstrumentKind.Plan, session, null, workspaceStore, workspaceState, args, goVerb,
            flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Plan);
        return true;
    }

        static bool TryDispatchArch(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.ArchDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.ArchDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.ArchDesk);
        return true;
    }


    static bool TryDispatchOnboard(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.OnboardDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.OnboardDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.OnboardDesk);
        return true;
    }

    static bool TryDispatchToolchain(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Toolchain))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Toolchain, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Toolchain);
        return true;
    }

    static bool TryDispatchFiles(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.FilesDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.FilesDesk, session, docStore, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.FilesDesk);
        return true;
    }

    static bool TryDispatchMdAuthor(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.MdAuthor))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.MdAuthor, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.MdAuthor);
        return true;
    }

    static bool TryDispatchLearn(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Learn))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Learn, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Learn);
        return true;
    }

    static bool TryDispatchProjectSwitch(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.ProjectSwitch))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.ProjectSwitch, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.ProjectSwitch);
        return true;
    }

    static bool TryDispatchIgnite(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.IgniteDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.IgniteDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.IgniteDesk);
        return true;
    }

    static bool TryDispatchWebcam(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.WebcamDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.WebcamDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.WebcamDesk);
        return true;
    }

    static bool TryDispatchPressure(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.PressureDesk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.PressureDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.PressureDesk);
        return true;
    }

    static bool TryDispatchDomain(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Domain))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Domain, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Domain);
        return true;
    }

    static bool TryDispatchCalendar(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Calendar))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Calendar, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Calendar);
        return true;
    }

    static bool TryDispatchRules(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Rules))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Rules, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Rules);
        return true;
    }

    static bool TryDispatchInventory(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Inventory))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Inventory, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Inventory);
        return true;
    }

    static bool TryDispatchVerifyWave(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.VerifyWave))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.VerifyWave, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.VerifyWave);
        return true;
    }

    static bool TryDispatchPs1(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftInstrumentKind.Ps1Desk))
            return false;

        goResult = SoftBoard(SoftInstrumentKind.Ps1Desk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftInstrumentKind.Ps1Desk);
        return true;
    }
}
