#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Desk — table-driven SoftFL densify (SoftInstrument invent REJECT).</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteDesk(string raw)
    {
        if (IsTestPlanIntent(raw))
            return RouteTestPlan(raw);
        if (IsTestSceneIntent(raw))
            return RouteTestScene(raw);
        if (IsEditorSceneIntent(raw))
            return RouteEditorScene(raw);
        if (IsManIntent(raw))
            return RouteMan(raw);
        if (IsHealthIntent(raw))
            return RouteHealth(raw);
        if (IsDialogMemoryIntent(raw))
            return RouteDialogMemory(raw);
        if (IsContextIntent(raw))
            return RouteContext(raw);
        if (IsQualityIntent(raw))
            return RouteQuality(raw);
        if (IsSessionIntent(raw))
            return RouteSession(raw);
        if (IsToolsIntent(raw))
            return RouteTools(raw);
        if (IsCapabilitiesIntent(raw))
            return RouteCapabilities(raw);
        if (IsCockpitIntent(raw))
            return RouteCockpit(raw);
        if (IsWorkIntent(raw))
            return RouteWork(raw);
        if (IsSaIntent(raw))
            return RouteSa(raw);
        if (IsLearnIntent(raw))
            return RouteLearn(raw);
        if (IsRefactorIntent(raw))
            return RouteRefactor(raw);
        if (IsElicitIntent(raw))
            return RouteElicit(raw);
        return null;
    }
}
