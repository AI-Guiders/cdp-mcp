#nullable enable
using System.Net.Http;
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
    /// Honest compaction hint — host may summarize without warning; pairs with cdp_pressure stash + memo line.
    /// </summary>
    internal const string ChargeAmnesiaPostfix =
        """

        ---
        If you feel completely lost / thread amnesia: compaction likely happened.
        Restore: cdp_pressure op=recall (hot stash → gate pull) · op=reconcile|align|ready · op=line (memo history).
        Also: %LocalAppData%/cdp-mcp/…/pressure-LATEST.md · pressure-memo-LATEST.md
        Then: habitat=CDP; re-read pressure axes (AutoIgnition / Task Manager / Domain / next); self-steer on reconcile when SSOT suffices.
        """;

    /// <summary>Provider cyber-policy: scrub shell tokens if legacy/custom text reaches inject.</summary>
    static readonly Regex ShellWord = new(@"\bshell\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string ComposeArmFireCharge() =>
        SanitizeComposerCharge(CanonicalComposerCharge + ChargeAmnesiaPostfix);

    /// <summary>Lead line for hard-remount boot wake — agent hears "initialized", not just silent DeskWarm.</summary>
    internal const string RemountInitializedLead =
        "MCP remounted / initialized.";

    /// <summary>Lead line after Cursor guest-host OOM / window terminate recovery.</summary>
    internal const string OomWakeLead =
        "reason=oom — Cursor host OOM / window terminated — recovered. Habitat=CDP. Run cdp_pressure op=recall then resume.";

    internal static string ComposeRemountInitializedCharge(string? projectRoot = null, string? focusHint = null)
    {
        var core = RemountInitializedLead + " " + CanonicalComposerCharge + ChargeAmnesiaPostfix;
        var domain = IdeDomainPulse.RemountDomainAppendix(projectRoot, focusHint);
        if (domain.Length == 0)
            return SanitizeComposerCharge(core);
        return SanitizeComposerCharge(core + "\n\n---\n" + domain);
    }

    internal static string ComposeOomWakeCharge(string? projectRoot = null, string? focusHint = null)
    {
        var core = OomWakeLead + " " + CanonicalComposerCharge + ChargeAmnesiaPostfix;
        var domain = IdeDomainPulse.RemountDomainAppendix(projectRoot, focusHint);
        if (domain.Length == 0)
            return SanitizeComposerCharge(core);
        return SanitizeComposerCharge(core + "\n\n---\n" + domain);
    }

    /// <summary>Cheap CDT liveness — /json/version without Composer attach.</summary>
    internal static async Task<bool> TryPingCdtAsync(int port, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var origin = $"http://127.0.0.1:{port}";
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", origin);
            using var resp = await http.GetAsync(origin + "/json/version", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Short wake when sync CallTool exceeds timeout_wake — not full continuity resume.</summary>
    internal static string ComposeToolWatchWakeCharge(string tool, int thresholdSeconds)
    {
        var name = string.IsNullOrWhiteSpace(tool) ? "(tool)" : tool.Trim();
        var sec = Math.Max(1, thresholdSeconds);
        return SanitizeComposerCharge(
            $"Tool call still running past wake threshold: {name} >{sec}s. Habitat=CDP. Check share from=self / cdp_pressure op=recall. Prefer wait for result or abort stuck host turn."
            + ChargeAmnesiaPostfix);
    }

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

    /// <summary>
    /// True when Composer text is an AutoIgnition wake charge — not human return.
    /// HILD must not clear away-latch on these (else Stop→Voice thrash).
    /// </summary>
    internal static bool LooksLikeAutoIgnitionCharge(string? text)
    {
        var t = (text ?? "").Replace('\u00a0', ' ').Trim();
        if (t.Length == 0)
            return false;

        if (t.Contains(CanonicalComposerCharge, StringComparison.Ordinal))
            return true;
        if (t.StartsWith(RemountInitializedLead, StringComparison.Ordinal))
            return true;
        if (t.StartsWith(OomWakeLead, StringComparison.Ordinal)
            || t.StartsWith("reason=oom", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Cursor host OOM", StringComparison.Ordinal))
            return true;
        if (t.StartsWith("Tool call still running past wake threshold:", StringComparison.Ordinal))
            return true;

        return false;
    }
}
