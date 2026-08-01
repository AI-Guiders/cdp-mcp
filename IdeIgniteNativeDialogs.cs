#nullable enable

namespace CdpMcp;

/// <summary>
/// Native Cursor/VS Code stall / OOM dialogs (Electron <c>showMessageBox</c>).
/// Not in Composer DOM — CDT cannot click them.
/// </summary>
internal static partial class IdeIgniteNativeDialogs
{
    const int BmClick = 0x00F5;
    const int MaxEnum = 2000;

    /// <summary>Public for tests — VS Code stall copy (not Win32 "End task").</summary>
    internal static bool LooksLikeStallMessage(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("not responding", StringComparison.OrdinalIgnoreCase)
        && (text.Contains("keep waiting", StringComparison.OrdinalIgnoreCase)
            || text.Contains("reopen", StringComparison.OrdinalIgnoreCase)
            || text.Contains("you can reopen", StringComparison.OrdinalIgnoreCase));

    /// <summary>Public for tests — button label after mnemonic strip.</summary>
    internal static bool IsKeepWaitingLabel(string? label)
    {
        var t = StripMnemonic(label);
        return t.Equals("Keep Waiting", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Wait", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cursor crash dialog: "The window terminated unexpectedly (reason: 'oom', code: …)".
    /// </summary>
    internal static bool LooksLikeOomTerminatedMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!text.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("window terminated", StringComparison.OrdinalIgnoreCase))
            return false;
        return text.Contains("oom", StringComparison.OrdinalIgnoreCase)
               || text.Contains("reason: 'oom'", StringComparison.OrdinalIgnoreCase)
               || text.Contains("reason: \"oom\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// OOM recovery — same-window <c>Reopen</c> only. Never New Window
    /// (operator: empty desk makes return harder). Dogfood 2026-07-31: Reopen + Close.
    /// </summary>
    internal static bool IsNewWindowLabel(string? label)
    {
        var t = StripMnemonic(label);
        return t.Equals("Reopen", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Reopen Window", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Reopen the window", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// When DirectUI/Chromium hides the oom body, crash dialog still exposes
    /// Reopen + Close and not Keep Waiting (stall has Keep Waiting).
    /// </summary>
    internal static bool LooksLikeOomRecoveryButtons(IReadOnlyList<string>? labels)
    {
        if (labels is null || labels.Count == 0)
            return false;

        var hasReopen = false;
        var hasClose = false;
        var hasKeepWaiting = false;
        foreach (var raw in labels)
        {
            if (IsNewWindowLabel(raw))
                hasReopen = true;
            else if (IsKeepWaitingLabel(raw))
                hasKeepWaiting = true;
            else if (StripMnemonic(raw).Equals("Close", StringComparison.OrdinalIgnoreCase))
                hasClose = true;
        }

        return hasReopen && hasClose && !hasKeepWaiting;
    }

    internal static string StripMnemonic(string? raw) =>
        (raw ?? "").Replace("&", "", StringComparison.Ordinal).Trim();

    public static bool TryClickKeepWaiting()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return TryClickLabeledButtonWindows(
                LooksLikeStallMessage,
                IsKeepWaitingLabel,
                "stall-dialog",
                buttonShapeFallback: null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] stall-dialog probe failed: {ex.Message}");
            return false;
        }
    }

    public static bool TryClickOomNewWindow()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return TryClickLabeledButtonWindows(
                LooksLikeOomTerminatedMessage,
                IsNewWindowLabel,
                "oom-dialog",
                buttonShapeFallback: LooksLikeOomRecoveryButtons);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] oom-dialog probe failed: {ex.Message}");
            return false;
        }
    }
}
