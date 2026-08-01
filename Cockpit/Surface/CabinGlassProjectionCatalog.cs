#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>
/// Cabin tool map (0-sync): SoftOrgan / seat pin → CIDE glass affordance.
/// CDP remains canon; glass only consumes derived mfd_page / chrome_hint.
/// Every SoftOrganKind go-pin must resolve (chrome stub OK) — catalog gate test.
/// </summary>
public static class CabinGlassProjectionCatalog
{
    public readonly record struct Projection(string? MfdPage, string? ChromeHint);

    /// <summary>Resolve canonical or alias organ pin to glass projection.</summary>
    public static Projection? TryResolve(string? organPin)
    {
        if (string.IsNullOrWhiteSpace(organPin))
            return null;

        var pin = organPin.Trim().ToLowerInvariant();
        if (pin.EndsWith("_scene", StringComparison.Ordinal))
            pin = pin[..^"_scene".Length];

        return pin switch
        {
            "shell" or "terminal" => new Projection("Terminal", null),
            "quality" or "gates" or "problems" or "problem" or "errlist" or "diags"
                => new Projection("Problems", null),
            "browser" or "internet_browser" or "scene_internet_browser"
                => new Projection("WebAiPortal", null),
            "build" or "build_desk" or "ship" or "ship_desk" => new Projection("Build", null),
            "test" or "test_desk" => new Projection("Tests", null),
            "debug" or "debug_desk" => new Projection("DebugStack", null),
            "git" or "git_scene" => new Projection("Git", null),
            "files" or "files_desk" or "explorer" or "fm" => new Projection("SolutionExplorer", null),
            "correspondence" or "crs" => new Projection("Correspondence", null),
            "hybrid_index" or "hci" or "codebase_index" => new Projection("HybridIndex", null),
            "related" or "related_files" => new Projection("RelatedFiles", null),
            "find_desk" or "search_desk" or "code_search" or "cdp_search"
                => new Projection("RelatedFiles", "agent · M: find"),
            "markdown" or "md_preview" => new Projection("MarkdownPreview", null),
            "md_author" or "md_author_desk"
                => new Projection("MarkdownPreview", "agent · M: md_author"),
            "options" or "settings" or "ai_chat_settings"
                => new Projection("AiChatSettings", null),
            "ignite" or "ignite_desk" or "autoignite"
                => new Projection("AiChatSettings", "agent · M: ignite"),
            "pressure" or "pressure_desk" or "compact_prep" or "pre_compact"
                => new Projection(null, "agent · M: pressure"),
            "onboard" or "onboard_desk" or "explore_desk"
                => new Projection(null, "agent · M: onboard"),
            "arch" or "arch_desk" or "arch_board"
                => new Projection("SemanticMap", "agent · M: arch"),
            "mcp" or "mcp_scene" => new Projection("AiChatSettings", "agent · M: mcp"),
            "plan" or "work" or "tm" or "tasks"
                => new Projection(null, "agent · P: plan"),
            "ps1" or "ps1_desk" or "ise" => new Projection("Terminal", "agent · M: ps1"),
            "report" or "evidence" or "pfd"
                => new Projection(null, "agent · M: report"),
            "sa_desk" or "code_sa" or "pre_sa" or "sa_code" or "cdp_sa"
                => new Projection(null, "agent · M: sa"),
            "crm" or "callout" or "crm_panel"
                => new Projection(null, "agent · M: crm"),
            "webcam" or "webcam_desk" or "camera" or "sense"
                => new Projection(null, "agent · M: webcam"),
            "toolchain" or "toolchain_desk"
                => new Projection(null, "agent · M: toolchain"),
            "alert" or "eicas" or "sa"
                => new Projection(null, "agent · M: alert"),
            "plugins" or "plugin" or "vsix"
                => new Projection(null, "agent · M: plugins"),
            "refactor" or "refactor_plan" or "debt"
                => new Projection(null, "agent · M: refactor"),
            "sys" => new Projection(null, "agent · M: sys"),
            "ecl" => new Projection(null, "agent · M: ecl"),
            "qrh" => new Projection(null, "agent · M: qrh"),
            "review" => new Projection(null, "agent · M: review"),
            "learn" => new Projection("MarkdownPreview", "agent · M: learn"),
            "domain" or "cdp_domain" or "ownership"
                => new Projection("MarkdownPreview", "agent · M: domain"),
            "project_switch" or "ps" or "scope_desk"
                => new Projection(null, "agent · M: project_switch"),
            _ => null
        };
    }
}
