#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent BATCH-9 soft-organ hosts — Ide*Channel with fused SoftOrganSeatExtras.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? ReportHandleOverride { get; set; }
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? DebugSaHandleOverride { get; set; }
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? TestSaHandleOverride { get; set; }
    internal static Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? BuildSaHandleOverride { get; set; }
    internal static Func<SessionContext, object>? SysHandleOverride { get; set; }
    internal static Func<IdeChkChannel.ProbeCtx, IReadOnlyDictionary<string, JsonElement>, object>? EclHandleOverride { get; set; }
    internal static Func<IdeReviewChannel.Inputs, IReadOnlyDictionary<string, JsonElement>, object>? ReviewHandleOverride { get; set; }
    internal static Func<IdeAlertChannel.Inputs, IReadOnlyDictionary<string, JsonElement>, object>? AlertHandleOverride { get; set; }
    internal static Func<SessionContext, DocumentBufferStore, SoftOrganSeatExtras?>? SeatExtrasOverride { get; set; }

    static Applied RunReport(CitizenIntentRouter.Route route)
    {
        var op = route.Op ?? "scene";
        var session = SessionResolver?.Invoke();
        if (session is null && ReportHandleOverride is null)
            return SoftOrganFail(route, "report", "report", "no_session");

        var args = BuildSoftOrganArgs(route, op);
        try
        {
            object result = ReportHandleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : IdeReportBoard.Handle(session!, args);
            return FinishSoftOrgan(route, result, "report", "report", "report", "report", op, route.Path);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, "report", "report", ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied RunDebugSa(CitizenIntentRouter.Route route) =>
        RunSaDesk(route, DebugSaHandleOverride, IdeDebugSaChannel.Handle, "debug_sa", "debug_desk", "debug_desk");

    static Applied RunTestSa(CitizenIntentRouter.Route route) =>
        RunSaDesk(route, TestSaHandleOverride, IdeTestSaChannel.Handle, "test_sa", "test_desk", "test_desk");

    static Applied RunBuildSa(CitizenIntentRouter.Route route) =>
        RunSaDesk(route, BuildSaHandleOverride, IdeBuildSaChannel.Handle, "build_sa", "build_desk", "build_desk");

    static Applied RunSaDesk(
        CitizenIntentRouter.Route route,
        Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object>? handleOverride,
        Func<SessionContext, IReadOnlyDictionary<string, JsonElement>, object> live,
        string action,
        string go,
        string placeOrgan)
    {
        var op = route.Op ?? "pulse";
        var session = SessionResolver?.Invoke();
        if (session is null && handleOverride is null)
            return SoftOrganFail(route, action, go, "no_session");

        var args = BuildSoftOrganArgs(route, op);
        try
        {
            object result = handleOverride is { } ov
                ? ov(session ?? new SessionContext(), args)
                : live(session!, args);
            return FinishSoftOrgan(route, result, action, go, placeOrgan, go, op, route.Path);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, action, go, ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied RunSys(CitizenIntentRouter.Route route)
    {
        var op = route.Op ?? "scene";
        var session = SessionResolver?.Invoke();
        if (session is null && SysHandleOverride is null)
            return SoftOrganFail(route, "sys", "sys", "no_session");

        try
        {
            object result;
            if (SysHandleOverride is { } ov)
            {
                result = ov(session ?? new SessionContext());
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                    return SoftOrganFail(route, "sys", "sys", "doc_store_unbound");
                var extras = ResolveSeatExtras(session!, store);
                if (extras is null)
                    return SoftOrganFail(route, "sys", "sys", "seat_extras_unavailable");
                result = extras.Value.SysBoard();
            }

            return FinishSoftOrgan(route, result, "sys", "sys", "sys", "sys", op);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, "sys", "sys", ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied RunEcl(CitizenIntentRouter.Route route)
    {
        var op = route.Op ?? "run";
        var session = SessionResolver?.Invoke();
        if (session is null && EclHandleOverride is null)
            return SoftOrganFail(route, "ecl", "ecl", "no_session");

        var store = IdeLanguageTools.TryGetDocumentStore();
        if (store is null && EclHandleOverride is null)
            return SoftOrganFail(route, "ecl", "ecl", "doc_store_unbound");

        var args = BuildSoftOrganArgs(route, op);
        try
        {
            object result;
            if (EclHandleOverride is { } ov)
                result = ov(IdeChkChannel.CtxFrom(session ?? new SessionContext(), false, true, false, false, false, false, true, false, false, true), args);
            else
            {
                var extras = ResolveSeatExtras(session!, store!);
                if (extras is null)
                    return SoftOrganFail(route, "ecl", "ecl", "seat_extras_unavailable");
                result = IdeChkChannel.Handle(extras.Value.ChkCtx, args);
            }

            return FinishSoftOrgan(route, result, "ecl", "ecl", "ecl", "ecl", op, route.Path);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, "ecl", "ecl", ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied RunReview(CitizenIntentRouter.Route route)
    {
        var op = route.Op ?? "board";
        var session = SessionResolver?.Invoke();
        if (session is null && ReviewHandleOverride is null)
            return SoftOrganFail(route, "review", "review", "no_session");

        var store = IdeLanguageTools.TryGetDocumentStore();
        if (store is null && ReviewHandleOverride is null)
            return SoftOrganFail(route, "review", "review", "doc_store_unbound");

        var args = BuildSoftOrganArgs(route, op);
        try
        {
            object result;
            if (ReviewHandleOverride is { } ov)
                result = ov(new IdeReviewChannel.Inputs(session ?? new SessionContext(), false, 0, false, 0, 0, null), args);
            else
            {
                var extras = ResolveSeatExtras(session!, store!);
                if (extras is null)
                    return SoftOrganFail(route, "review", "review", "seat_extras_unavailable");
                var inputs = new IdeReviewChannel.Inputs(
                    session!,
                    extras.Value.GitDirty,
                    extras.Value.Problems.Errors,
                    extras.Value.TestsFailed,
                    extras.Value.Quality.Fail,
                    extras.Value.Quality.Warn,
                    extras.Value.ChkSnap);
                result = IdeReviewChannel.Handle(inputs, args);
            }

            return FinishSoftOrgan(route, result, "review", "review", "review", "review", op, route.Path);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, "review", "review", ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied RunAlert(CitizenIntentRouter.Route route)
    {
        var op = route.Op ?? "pulse";
        var session = SessionResolver?.Invoke();
        if (session is null && AlertHandleOverride is null)
            return SoftOrganFail(route, "alert", "alert", "no_session");

        var store = IdeLanguageTools.TryGetDocumentStore();
        if (store is null && AlertHandleOverride is null)
            return SoftOrganFail(route, "alert", "alert", "doc_store_unbound");

        var args = BuildSoftOrganArgs(route, op);
        try
        {
            object result;
            if (AlertHandleOverride is { } ov)
                result = ov(default, args);
            else
            {
                var extras = ResolveSeatExtras(session!, store!);
                if (extras is null)
                    return SoftOrganFail(route, "alert", "alert", "seat_extras_unavailable");
                result = IdeAlertChannel.Handle(extras.Value.AlertInputs, args);
            }

            return FinishSoftOrgan(route, result, "alert", "alert", placeOrgan: null, "alert", op);
        }
        catch (Exception ex)
        {
            return SoftOrganFail(route, "alert", "alert", ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Applied SoftOrganFail(CitizenIntentRouter.Route route, string action, string go, string reason) =>
        new(route.Raw, route.Verb.ToString(), Ok: false, Action: action, Go: go, Reason: reason);

    static Applied FinishSoftOrgan(
        CitizenIntentRouter.Route route,
        object result,
        string action,
        string go,
        string? placeOrgan,
        string tag,
        string op,
        string? path = null)
    {
        var json = result is string s ? s : JsonSerializer.Serialize(result);
        var ok = TryReadSoftOrganOk(json);
        var pulse = TryReadSoftOrganPulse(json, tag, op);
        string? seat = placeOrgan is { Length: > 0 } ? IdeDeskSeats.PlaceOrgan(placeOrgan) : null;
        return new Applied(
            route.Raw,
            route.Verb.ToString(),
            Ok: ok,
            Action: action,
            Seat: seat,
            Go: go,
            Path: path,
            Pulse: pulse,
            Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? action + "_failed"));
    }

    static SoftOrganSeatExtras? ResolveSeatExtras(SessionContext session, DocumentBufferStore store) =>
        SeatExtrasOverride?.Invoke(session, store)
        ?? IdeCockpit.TryBuildCitizenSeatExtras(session, store, ShellHabitatResolver);

    static Dictionary<string, JsonElement> BuildSoftOrganArgs(CitizenIntentRouter.Route route, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        var raw = route.Raw;
        foreach (var key in SoftOrganArgKeys)
            PutIfPresent(args, key, CitizenIntentRouter.ExtractKeyedValue(raw, key));

        if (route.Path is { Length: > 0 } path && !args.ContainsKey("path") && !args.ContainsKey("id"))
            PutIfPresent(args, "path", path);

        if (route.Op is "pulse" or "slim" or "full" && !args.ContainsKey("depth"))
            PutIfPresent(args, "depth", route.Op);

        return args;
    }

    static readonly string[] SoftOrganArgKeys =
    [
        "depth", "scope", "path", "id", "item", "checklist", "file", "q", "query", "link", "phase"
    ];
}
