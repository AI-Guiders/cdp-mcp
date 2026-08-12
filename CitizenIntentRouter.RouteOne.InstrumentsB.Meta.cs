#nullable enable

namespace CdpMcp;

/// <summary>InstrumentsB meta/desk tail — SoftFL peel off RouteOne.InstrumentsB.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteInstrumentsBMeta(string raw)
    {
        if (raw.Equals("domain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("domain_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("domain_card ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_domain_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_domain_card ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteDomain(raw);
        }

        if (raw.Equals("rules", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rules_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("standing", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("standing ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("healthy_agent", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("healthy_agent ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_rules", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_rules ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rules_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rules_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rules_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("rules_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("rules_card ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_rules_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_rules_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_rules_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_rules_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_rules_list", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_rules_list ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_rules_card", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_rules_card ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteRules(raw);
        }

        if (raw.Equals("inventory", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("inventory ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("inventory_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("inventory_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gaps", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gaps ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_inventory", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_inventory ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("inventory_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("inventory_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("inventory_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("inventory_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_inventory_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_inventory_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_inventory_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_inventory_pulse ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteInventory(raw);
        }

        if (raw.Equals("verify_wave", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("verify_wave ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("verify_wave_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("verify_wave_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("wave_verify", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("wave_verify ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_verify_wave", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_verify_wave ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("verify_wave_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("verify_wave_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("verify_wave_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("verify_wave_pulse ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_verify_wave_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_verify_wave_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_verify_wave_pulse", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_verify_wave_pulse ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteVerifyWave(raw);
        }

        if (IsCrmIntent(raw))
            return RouteCrm(raw);

        if (IsArchIntent(raw))
            return RouteArch(raw);

        if (IsMdAuthorIntent(raw))
            return RouteMdAuthor(raw);

        if (IsScopeIntent(raw))
            return RouteScope(raw);

        if (IsGlassIntent(raw))
            return RouteGlass(raw);

        if (IsFdrIntent(raw))
            return RouteFdr(raw);

        if (IsTeethIntent(raw))
            return RouteTeeth(raw);

        if (IsPostmortemIntent(raw))
            return RoutePostmortem(raw);

        if (IsPluginsIntent(raw))
            return RoutePlugins(raw);

        if (IsProblemsIntent(raw))
            return RouteProblems(raw);

        if (IsReportIntent(raw))
            return RouteReport(raw);

        if (IsDebugSaIntent(raw))
            return RouteDebugSa(raw);

        if (IsTestSaIntent(raw))
            return RouteTestSa(raw);

        if (IsBuildSaIntent(raw))
            return RouteBuildSa(raw);

        if (IsSysIntent(raw))
            return RouteSys(raw);

        if (IsEclIntent(raw))
            return RouteEcl(raw);

        if (IsReviewIntent(raw))
            return RouteReview(raw);

        if (IsAlertIntent(raw))
            return RouteAlert(raw);

        return null;
    }
}
