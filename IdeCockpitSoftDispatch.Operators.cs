#nullable enable
using System.Text.Json;
using Cdp.Core;
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
        if (!IsGo(goVerb, "crm", "callout", "crm_panel"))
            return false;

        goResult = IdeCrmChannel.Handle(session, workspaceStore, workspaceState, args);
        PlaceAndClear(ref goVerb, "crm");
        return true;
    }

    static bool TryDispatchArch(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "arch_desk", "arch_board", "board", "sketch_desk", "cdp_arch"))
            return false;

        goResult = IdeArchBoardChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "arch_desk");
        return true;
    }

    static bool TryDispatchFiles(
        ref string? goVerb,
        ref object? goResult,
        DocumentBufferStore docStore,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "files_desk", "files", "explorer", "fm", "file_manager"))
            return false;

        goResult = IdeFilesChannel.Handle(docStore, session, args);
        PlaceAndClear(ref goVerb, "files_desk");
        return true;
    }

    static bool TryDispatchIgnite(
        ref string? goVerb,
        ref object? goResult,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "ignite_desk", "ignite", "autoignite", "cdt_ignite", "cdp_ignite"))
            return false;

        goResult = IdeIgniteChannel.Handle(args);
        PlaceAndClear(ref goVerb, "ignite_desk");
        return true;
    }

    static bool TryDispatchWebcam(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "webcam_desk", "webcam", "camera", "sense", "cdp_webcam"))
            return false;

        goResult = IdeWebcamChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "webcam_desk");
        return true;
    }

    static bool TryDispatchPressure(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "pressure_desk", "pressure", "compact_prep", "pre_compact", "cdp_pressure"))
            return false;

        goResult = IdePressureChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "pressure_desk");
        return true;
    }
}
