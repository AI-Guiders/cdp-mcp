#nullable enable

namespace CdpMcp;

/// <summary>RouteOne family gate: Desk — peel method_lines off RouteOne.</summary>
internal static partial class CitizenIntentRouter
{
    static Route? TryRouteDesk(string raw)
    {
        if (raw.Equals("test_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_apply ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_draft", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_draft ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_plan_run", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_plan_run ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan_preview", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan_preview ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_plan_apply", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_plan_apply ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTestPlan(raw);
        }

        if (raw.Equals("test_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_scene_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_scene_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_test_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_test_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("test_runner", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("test_runner ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTestScene(raw);
        }

        if (raw.Equals("editor_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor_scene_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_scene_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_editor_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_editor_scene ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("editor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("editor ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteEditorScene(raw);
        }

        if (raw.Equals("man", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("man ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("man_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("man_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_man", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_man ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("manual", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("manual ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteMan(raw);
        }

        if (raw.Equals("health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("health ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("health_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("health_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_health ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("ops_health", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ops_health ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteHealth(raw);
        }

        if (raw.Equals("context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("context_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("context_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_context ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_context", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_context ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteContext(raw);
        }

        if (raw.Equals("quality", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_gates", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_gates ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_quality", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_quality ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_assert", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_assert ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_assertions", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_assertions ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_adx", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_adx ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_project", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_project ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("quality_map", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("quality_map ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates_disk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates_disk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("gates_assert", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("gates_assert ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteQuality(raw);
        }

        if (raw.Equals("session", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("session_plane", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("session_plane ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_session", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_session ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteSession(raw);
        }

        if (raw.Equals("tools", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tools_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("tools_palette", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("tools_palette ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_tools", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_tools ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("palette", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("palette ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteTools(raw);
        }

        if (raw.Equals("capabilities", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("capabilities ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("capabilities_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("capabilities_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_capabilities", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_capabilities ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("caps", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("caps ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCapabilities(raw);
        }

        if (raw.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cockpit_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cockpit_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_cockpit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_cockpit ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("agent_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("agent_desk ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteCockpit(raw);
        }

        if (raw.Equals("work", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("work ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("work_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("work_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_work", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_work ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("intent_workspace", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("intent_workspace ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteWork(raw);
        }

        if (raw.Equals("sa", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sa ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sa_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sa_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_sa", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_sa ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("code_sa", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("code_sa ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("pre_sa", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("pre_sa ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sa_code", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("sa_code ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteSa(raw);
        }

        if (raw.Equals("learn", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("learn ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("learn_desk", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("learn_desk ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_learn", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_learn ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("learning", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("learning ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteLearn(raw);
        }

        if (raw.Equals("refactor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("refactor ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("refactor_plan", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("refactor_plan ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_refactor", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_refactor ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("debt_scene", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("debt_scene ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteRefactor(raw);
        }

        if (raw.Equals("elicit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("elicit ", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("cdp_elicit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("cdp_elicit ", StringComparison.OrdinalIgnoreCase))
        {
            return RouteElicit(raw);
        }

        return null;
    }
}
