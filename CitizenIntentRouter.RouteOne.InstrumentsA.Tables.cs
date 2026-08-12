#nullable enable

namespace CdpMcp;

/// <summary>RouteOne InstrumentsA gate tables — SoftFL densify (reuse MatchesIntent + organ Compounds; SoftInstrument invent REJECT).</summary>
internal static partial class CitizenIntentRouter
{
    // IcmCompounds — SSOT in CitizenIntentRouter.Icm.cs
    static readonly string[] IcmAliases =
        ["cdp_icm", "command_module"];
    static bool IsIcmIntent(string raw) =>
        MatchesIntent(raw, "icm", IcmAliases, IcmCompounds);

    // FilesCompounds — SSOT in CitizenIntentRouter.Files.cs
    static readonly string[] FilesAliases =
        ["cdp_files", "file_manager", "fm"];
    static bool IsFilesIntent(string raw) =>
        MatchesIntent(raw, "files", FilesAliases, FilesCompounds);

    // OnboardCompounds — SSOT in CitizenIntentRouter.Onboard.cs
    static readonly string[] OnboardAliases =
        ["explore", "cdp_onboard"];
    static bool IsOnboardIntent(string raw) =>
        MatchesIntent(raw, "onboard", OnboardAliases, OnboardCompounds);

    // PeelCompounds — SSOT in CitizenIntentRouter.Peel.cs
    static readonly string[] PeelAliases =
        ["cdp_peel"];
    static bool IsPeelIntent(string raw) =>
        MatchesIntent(raw, "peel", PeelAliases, PeelCompounds);

    // EditPlanCompounds — SSOT in CitizenIntentRouter.EditPlan.cs
    static readonly string[] EditPlanAliases =
        ["cdp_edit_plan"];
    static bool IsEditPlanIntent(string raw) =>
        MatchesIntent(raw, "edit_plan", EditPlanAliases, EditPlanCompounds);

    // AnalysisCompounds — SSOT in CitizenIntentRouter.Analysis.cs
    static readonly string[] AnalysisAliases = [];
    static bool IsAnalysisIntent(string raw) =>
        MatchesIntent(raw, "analysis", AnalysisAliases, AnalysisCompounds);

    static readonly (string Prefix, string Op)[] PressureCompounds = [];
    static readonly string[] PressureAliases = [];
    static bool IsPressureIntent(string raw) =>
        MatchesIntent(raw, "pressure", PressureAliases, PressureCompounds);

    static readonly (string Prefix, string Op)[] CalendarCompounds = [];
    static readonly string[] CalendarAliases =
        ["clock", "calendar_desk"];
    static bool IsCalendarIntent(string raw) =>
        MatchesIntent(raw, "calendar", CalendarAliases, CalendarCompounds);

    // LandCompounds — SSOT in CitizenIntentRouter.Land.cs
    static readonly string[] LandAliases = [];
    static bool IsLandIntent(string raw) =>
        MatchesIntent(raw, "land", LandAliases, LandCompounds);

    // PkgCompounds — SSOT in CitizenIntentRouter.Pkg.cs
    static readonly string[] PkgAliases =
        ["nuget", "packages", "package"];
    static bool IsPkgIntent(string raw) =>
        MatchesIntent(raw, "pkg", PkgAliases, PkgCompounds);

    // ProjectCompounds — SSOT in CitizenIntentRouter.Project.cs
    static readonly string[] ProjectAliases =
        ["projects", "sln", "solution"];
    static bool IsProjectIntent(string raw) =>
        MatchesIntent(raw, "project", ProjectAliases, ProjectCompounds);
}
