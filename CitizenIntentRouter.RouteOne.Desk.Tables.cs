#nullable enable

namespace CdpMcp;

/// <summary>RouteOne Desk gate tables — SoftFL densify (reuse MatchesIntent; SoftOrgan invent REJECT).</summary>
internal static partial class CitizenIntentRouter
{
    // TestPlanCompounds lives in TestPlan.cs (normalize SSOT) — reuse for gate match.

    static readonly string[] TestSceneAliases =
        ["test_scene_desk", "cdp_test_scene", "test_runner"];
    static readonly (string Prefix, string Op)[] TestSceneCompounds = [];

    static readonly string[] EditorSceneAliases =
        ["editor_scene_desk", "cdp_editor_scene", "editor_desk", "editor"];
    static readonly (string Prefix, string Op)[] EditorSceneCompounds = [];

    static readonly string[] ManAliases = ["man_desk", "cdp_man", "manual"];
    static readonly (string Prefix, string Op)[] ManCompounds = [];

    static readonly string[] HealthAliases = ["health_desk", "cdp_health", "ops_health"];
    static readonly (string Prefix, string Op)[] HealthCompounds = [];

    static readonly string[] ContextAliases = ["context_desk", "cdp_context", "session_context"];
    static readonly (string Prefix, string Op)[] ContextCompounds = [];

    // QualityCompounds + QualityPrefixes live in Quality.cs — reuse for gate match.

    static readonly string[] SessionAliases = ["session_desk", "session_plane", "cdp_session"];
    static readonly (string Prefix, string Op)[] SessionCompounds = [];

    static readonly string[] ToolsAliases = ["tools_desk", "tools_palette", "cdp_tools", "palette"];
    static readonly (string Prefix, string Op)[] ToolsCompounds = [];

    static readonly string[] CapabilitiesAliases = ["capabilities_desk", "cdp_capabilities", "caps"];
    static readonly (string Prefix, string Op)[] CapabilitiesCompounds = [];

    static readonly string[] CockpitAliases = ["cockpit_desk", "cdp_cockpit", "agent_desk"];
    static readonly (string Prefix, string Op)[] CockpitCompounds = [];

    static readonly string[] WorkAliases = ["work_desk", "cdp_work", "intent_workspace"];
    static readonly (string Prefix, string Op)[] WorkCompounds = [];

    static readonly string[] SaAliases = ["sa_desk", "cdp_sa", "code_sa", "pre_sa", "sa_code"];
    static readonly (string Prefix, string Op)[] SaCompounds = [];

    static readonly string[] LearnAliases = ["learn_desk", "cdp_learn", "learning"];
    static readonly (string Prefix, string Op)[] LearnCompounds = [];

    static readonly string[] RefactorAliases = ["refactor_plan", "cdp_refactor", "debt_scene"];
    static readonly (string Prefix, string Op)[] RefactorCompounds = [];

    static readonly string[] ElicitAliases = ["cdp_elicit"];
    static readonly (string Prefix, string Op)[] ElicitCompounds = [];

    static bool IsTestPlanIntent(string raw) =>
        MatchesIntent(raw, "test_plan", [], TestPlanCompounds);
    static bool IsTestSceneIntent(string raw) =>
        MatchesIntent(raw, "test_scene", TestSceneAliases, TestSceneCompounds);
    static bool IsEditorSceneIntent(string raw) =>
        MatchesIntent(raw, "editor_scene", EditorSceneAliases, EditorSceneCompounds);
    static bool IsManIntent(string raw) =>
        MatchesIntent(raw, "man", ManAliases, ManCompounds);
    static bool IsHealthIntent(string raw) =>
        MatchesIntent(raw, "health", HealthAliases, HealthCompounds);
    static bool IsContextIntent(string raw) =>
        MatchesIntent(raw, "context", ContextAliases, ContextCompounds);
    static bool IsQualityIntent(string raw) =>
        MatchesIntent(raw, "quality", QualityPrefixes, QualityCompounds);
    static bool IsSessionIntent(string raw) =>
        MatchesIntent(raw, "session", SessionAliases, SessionCompounds);
    static bool IsToolsIntent(string raw) =>
        MatchesIntent(raw, "tools", ToolsAliases, ToolsCompounds);
    static bool IsCapabilitiesIntent(string raw) =>
        MatchesIntent(raw, "capabilities", CapabilitiesAliases, CapabilitiesCompounds);
    static bool IsCockpitIntent(string raw) =>
        MatchesIntent(raw, "cockpit", CockpitAliases, CockpitCompounds);
    static bool IsWorkIntent(string raw) =>
        MatchesIntent(raw, "work", WorkAliases, WorkCompounds);
    static bool IsSaIntent(string raw) =>
        MatchesIntent(raw, "sa", SaAliases, SaCompounds);
    static bool IsLearnIntent(string raw) =>
        MatchesIntent(raw, "learn", LearnAliases, LearnCompounds);
    static bool IsRefactorIntent(string raw) =>
        MatchesIntent(raw, "refactor", RefactorAliases, RefactorCompounds);
    static bool IsElicitIntent(string raw) =>
        MatchesIntent(raw, "elicit", ElicitAliases, ElicitCompounds);
}
