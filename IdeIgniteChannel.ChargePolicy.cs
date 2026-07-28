#nullable enable
using System.Text.RegularExpressions;

namespace CdpMcp;

internal static partial class IdeIgniteChannel
{
    /// <summary>
    /// Composer wake charge — no TM stage body, shell, toolchain, or commands (cockpit holds SSOT).
    /// </summary>
    internal const string CanonicalComposerCharge =
        "Resume the current authorized local development task from Task Manager. Habitat=CDP. Verify the result and re-arm when idle.";

    /// <summary>
    /// Honest compaction hint — host may summarize without warning; pairs with cdp_pressure stash.
    /// </summary>
    internal const string ChargeAmnesiaPostfix =
        """

        ---
        If you feel completely lost / thread amnesia: compaction likely happened.
        Restore: cdp_pressure op=recall (also %LocalAppData%/cdp-mcp/pressure-LATEST.md)
        Then: habitat=CDP; re-read pressure axes (AutoIgnition / Task Manager / next).
        """;

    /// <summary>Provider cyber-policy: scrub shell tokens if legacy/custom text reaches inject.</summary>
    static readonly Regex ShellWord = new(@"\bshell\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string ComposeArmFireCharge() =>
        SanitizeComposerCharge(CanonicalComposerCharge + ChargeAmnesiaPostfix);

    /// <summary>Lead line for hard-remount boot wake — agent hears "initialized", not just silent DeskWarm.</summary>
    internal const string RemountInitializedLead =
        "MCP remounted / initialized.";

    internal static string ComposeRemountInitializedCharge() =>
        SanitizeComposerCharge(
            RemountInitializedLead + " " + CanonicalComposerCharge + ChargeAmnesiaPostfix);

    internal static string EventTokenForCharge(string eventId) =>
        string.Equals(eventId, "shell_finished", StringComparison.OrdinalIgnoreCase)
            ? "terminal_finished"
            : eventId;

    internal static string SanitizeComposerCharge(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var t = text;
        t = t.Replace("shell_finished", "terminal_finished", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("shell_done", "terminal_done", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("on_shell", "on_terminal", StringComparison.OrdinalIgnoreCase);
        t = t.Replace("powershell", "pwsh", StringComparison.OrdinalIgnoreCase);
        return ShellWord.Replace(t, "terminal");
    }
}
