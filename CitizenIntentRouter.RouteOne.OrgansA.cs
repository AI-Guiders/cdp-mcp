#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: OrgansA (icm..project) — table-driven SoftFL densify (SoftOrgan invent REJECT).</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteOrgansA(string raw)
    {
        if (IsIcmIntent(raw))
            return RouteIcm(raw);
        if (IsFilesIntent(raw))
            return RouteFiles(raw);
        if (IsOnboardIntent(raw))
            return RouteOnboard(raw);
        if (IsPeelIntent(raw))
            return RoutePeel(raw);
        if (IsEditPlanIntent(raw))
            return RouteEditPlan(raw);
        if (IsAnalysisIntent(raw))
            return RouteAnalysis(raw);
        if (IsPressureIntent(raw))
            return RoutePressure(raw);
        if (IsCalendarIntent(raw))
            return RouteCalendar(raw);
        if (IsLandIntent(raw))
            return RouteLand(raw);
        if (IsPkgIntent(raw))
            return RoutePkg(raw);
        if (IsProjectIntent(raw))
            return RouteProject(raw);
        return null;
    }
}
