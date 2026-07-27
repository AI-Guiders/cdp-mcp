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
        if (!IsSoft(goVerb, SoftOrganKind.Crm))
            return false;

        goResult = SoftBoard(
            SoftOrganKind.Crm, session, null, workspaceStore, workspaceState, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.Crm);
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
        if (!IsSoft(goVerb, SoftOrganKind.Plan))
            return false;

        goResult = SoftBoard(
            SoftOrganKind.Plan, session, null, workspaceStore, workspaceState, args, goVerb);
        PlaceSoft(ref goVerb, SoftOrganKind.Plan);
        return true;
    }

        static bool TryDispatchArch(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.ArchDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.ArchDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.ArchDesk);
        return true;
    }


    static bool TryDispatchOnboard(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.OnboardDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.OnboardDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.OnboardDesk);
        return true;
    }

    static bool TryDispatchToolchain(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.Toolchain))
            return false;

        goResult = SoftBoard(SoftOrganKind.Toolchain, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.Toolchain);
        return true;
    }

    static bool TryDispatchFiles(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.FilesDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.FilesDesk, session, docStore, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.FilesDesk);
        return true;
    }

    static bool TryDispatchIgnite(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.IgniteDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.IgniteDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.IgniteDesk);
        return true;
    }

    static bool TryDispatchWebcam(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.WebcamDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.WebcamDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.WebcamDesk);
        return true;
    }

    static bool TryDispatchPressure(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.PressureDesk))
            return false;

        goResult = SoftBoard(SoftOrganKind.PressureDesk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.PressureDesk);
        return true;
    }

    static bool TryDispatchPs1(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsSoft(goVerb, SoftOrganKind.Ps1Desk))
            return false;

        goResult = SoftBoard(SoftOrganKind.Ps1Desk, session, null, null, null, args, flattenOrganArgs: true);
        PlaceSoft(ref goVerb, SoftOrganKind.Ps1Desk);
        return true;
    }
}
