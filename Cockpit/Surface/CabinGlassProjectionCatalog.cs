#nullable enable
namespace CdpMcp.Cockpit.Surface;

/// <summary>
/// Cabin tool map (0-sync phase 2): organ pin → CIDE glass affordance.
/// CDP remains canon; glass only consumes derived mfd_page / chrome_hint.
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
            "ignite" or "ignite_desk" or "autoignite"
                => new Projection("AiChatSettings", "agent · M: ignite"),
            "pressure" or "pressure_desk" or "compact_prep" or "pre_compact"
                => new Projection(null, "agent · M: pressure"),
            "ps1" or "ps1_desk" or "ise" => new Projection("Terminal", "agent · M: ps1"),
            _ => null
        };
    }
}
