#nullable enable

namespace CdpDeskTui;

/// <summary>Fixture desk pulse — peel0 density spike; live CDP wire = later.</summary>
internal sealed class DeskFixture
{
    public required string Banner { get; init; }
    public required string Alert { get; init; }
    public required string Plan { get; init; }
    public required string Forward { get; init; }
    public required string M { get; init; }
    public required string Hint { get; init; }
    public DateTimeOffset AtUtc { get; init; }

    public static DeskFixture Sample(DateTimeOffset? at = null) => new()
    {
        AtUtc = at ?? DateTimeOffset.UtcNow,
        Banner = "| P:plan | F:editor | M:shell |",
        Alert = "sa · clear · spike TUI (no live wire yet)",
        Plan =
            "Glass as context economy (0-sync)\n" +
            "› (pick task) · act\n" +
            "\n" +
            "Feature = Intent · Task = Stage\n" +
            "PF writes · PM steers\n" +
            "\n" +
            "r = refresh fixture\n" +
            "q / Ctrl+Q = quit",
        Forward =
            "editor · pulse\n" +
            "4 buf · snap\n" +
            "\n" +
            "(read-only peek)\n" +
            "IntelliSense stays in CDP organs\n" +
            "— not in this chrome",
        M =
            "shell · idle\n" +
            "\n" +
            "cdp_shell_* primary\n" +
            "terminal_* escape\n" +
            "\n" +
            "Multi-monitor tip:\n" +
            "run N processes\n" +
            "  --seat=p|f|m",
        Hint = "TUI desk spike · density over chrome · drag seat borders"
    };
}
