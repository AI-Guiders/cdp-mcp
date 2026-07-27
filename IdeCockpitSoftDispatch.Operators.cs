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

    static bool TryDispatchPlan(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IntentWorkspaceStore? workspaceStore,
        IntentWorkspaceState workspaceState,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "plan", "work", "tasks", "tm", "task", "feature", "promote", "confirm", "reject", "phase"))
            return false;

        if (workspaceStore is null)
        {
            goResult = new
            {
                ok = false,
                go = "plan",
                error = "no_workspace",
                hint = "Intent workspace WitDB unavailable."
            };
        }
        else
        {
            var tmArgs = new Dictionary<string, JsonElement>(args, StringComparer.Ordinal);
            if (session.ProjectRoot is { Length: > 0 } pr)
                tmArgs["project_root"] = JsonSerializer.SerializeToElement(pr);
            tmArgs["session_phase"] = JsonSerializer.SerializeToElement(CdpEnumParse.ToWire(session.Phase));
            if (!tmArgs.ContainsKey("tm_op")
                && goVerb is "feature" or "task" or "promote" or "confirm" or "reject"
                && (!tmArgs.TryGetValue("go_args", out var gax)
                    || gax.ValueKind != JsonValueKind.Object
                    || !gax.TryGetProperty("op", out _)))
            {
                tmArgs["tm_op"] = JsonSerializer.SerializeToElement(
                    goVerb.Equals("feature", StringComparison.OrdinalIgnoreCase) ? "feature"
                    : goVerb.Equals("task", StringComparison.OrdinalIgnoreCase) ? "task"
                    : goVerb.Equals("promote", StringComparison.OrdinalIgnoreCase) ? "promote"
                    : goVerb.Equals("confirm", StringComparison.OrdinalIgnoreCase) ? "confirm"
                    : goVerb.Equals("reject", StringComparison.OrdinalIgnoreCase) ? "reject"
                    : "board");
            }

            goResult = IdeTaskManager.Handle(workspaceStore, workspaceState, tmArgs);
        }

        PlaceAndClear(ref goVerb, "plan");
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

    static bool TryDispatchOnboard(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "onboard_desk", "explore_desk", "onboard", "explore", "cdp_onboard"))
            return false;

        goResult = IdeOnboardChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "onboard_desk");
        return true;
    }

    static bool TryDispatchToolchain(
        ref string? goVerb,
        ref object? goResult,
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!IsGo(goVerb, "toolchain", "toolchain_desk", "toolchain_ensure", "toolchain_probe",
                "toolchain_install", "toolchain_add", "cdp_toolchain"))
            return false;

        // GoMap defaults already merge op= for ensure/probe/…; Handle reads args.
        goResult = IdeToolchainChannel.Handle(session, args);
        PlaceAndClear(ref goVerb, "toolchain");
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
