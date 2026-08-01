namespace CdpMcp;

/// <summary>Layout preset maps layout id → seat → pin (≤ADX soft-warn peel).</summary>
internal static partial class IdeDeskSeats
{
    /// <summary>layout id → seat → pin (canonical go verb).</summary>
    static readonly Dictionary<string, Dictionary<string, string>> SeatPresets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cockpit"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["desk"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["code+net"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["code+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["code+git"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
            ["net+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["forward"] = "browser",
                ["m"] = "shell_scene",
            },
            ["code+net+shell"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "shell_scene",
                ["forward"] = "editor_scene",
                ["m"] = "browser",
            },
            ["agent"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "script_scene",
            },
            ["bug"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "problems",
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["verify"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "ecl",
                ["forward"] = "editor_scene",
                ["m"] = "shell_scene",
            },
            ["phase-review"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "review",
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
            ["phase-explore"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "onboard_desk",
            },
            ["phase-handoff"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "git_scene",
            },
            ["arch"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "plan",
                ["forward"] = "editor_scene",
                ["m"] = "arch_desk",
            },
            ["onboard"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "onboard_desk",
            },
            ["explore"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["p"] = "project_scene",
                ["forward"] = "editor_scene",
                ["m"] = "onboard_desk",
            },
        };
}
