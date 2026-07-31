#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CdpMcp;

/// <summary>
/// Native Cursor/VS Code stall dialog — Electron main <c>showMessageBox</c>
/// ("The window is not responding" / Reopen · Close · Keep Waiting).
/// Not the Windows OS hung-app dialog. Not in Composer DOM — CDT cannot click it.
/// </summary>
internal static class IdeIgniteNativeDialogs
{
    const int BmClick = 0x00F5;
    const int MaxEnum = 400;

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
    /// OOM recovery button — same-window <c>Reopen</c> only. Never empty New Window
    /// (operator: New Window makes return harder). Screenshot dogfood 2026-07-31:
    /// dialog buttons were Reopen + Close; original tooth only matched New Window → miss.
    /// </summary>
    internal static bool IsNewWindowLabel(string? label)
    {
        var t = StripMnemonic(label);
        // Do NOT match "New Window" / "New empty window" — opens empty desk.
        return t.Equals("Reopen", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Reopen Window", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Reopen the window", StringComparison.OrdinalIgnoreCase);
    }

    internal static string StripMnemonic(string? raw) =>
        (raw ?? "").Replace("&", "", StringComparison.Ordinal).Trim();

    /// <summary>
    /// Best-effort: find Cursor-owned stall message box and click Keep Waiting.
    /// No-op on non-Windows.
    /// </summary>
    public static bool TryClickKeepWaiting()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return TryClickLabeledButtonWindows(
                LooksLikeStallMessage,
                IsKeepWaitingLabel,
                "stall-dialog");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] stall-dialog probe failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Best-effort: OOM terminated dialog → New Window (tooth). No-op on non-Windows.
    /// </summary>
    public static bool TryClickOomNewWindow()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return TryClickLabeledButtonWindows(
                LooksLikeOomTerminatedMessage,
                IsNewWindowLabel,
                "oom-dialog");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] oom-dialog probe failed: {ex.Message}");
            return false;
        }
    }

    static bool TryClickLabeledButtonWindows(
        Func<string?, bool> looksLikeDialog,
        Func<string?, bool> isButtonLabel,
        string logTag)
    {
        var hits = new List<nint>();
        var count = 0;
        EnumWindows((hWnd, _) =>
        {
            if (count++ > MaxEnum)
                return false;
            if (!IsWindowVisible(hWnd))
                return true;

            var title = GetWindowText(hWnd);
            var blob = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(title))
                blob.Append(title).Append(' ');
            CollectChildText(hWnd, blob, depth: 0);

            var text = blob.ToString();
            if (!looksLikeDialog(text)
                && !(logTag == "stall-dialog"
                     && text.Contains("not responding", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!IsCursorLikeOwner(hWnd) && !looksLikeDialog(text))
                return true;

            if (!TryFindButtonByLabel(hWnd, isButtonLabel, out var button))
            {
                if (looksLikeDialog(text))
                {
                    var labels = CollectButtonLabels(hWnd);
                    Console.Error.WriteLine(
                        $"[ide_ignite] {logTag} matched text but no recovery button; labels=[{string.Join(" | ", labels)}]");
                }

                return true;
            }

            hits.Add(button);
            return false;
        }, 0);

        if (hits.Count == 0)
            return false;

        _ = SendMessage(hits[0], BmClick, 0, 0);
        return true;
    }

    static bool TryFindButtonByLabel(nint root, Func<string?, bool> isLabel, out nint button)
    {
        nint found = 0;
        EnumChildWindows(root, (hWnd, _) =>
        {
            var label = GetWindowText(hWnd);
            if (isLabel(label))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, 0);

        button = found;
        return found != 0;
    }

    static List<string> CollectButtonLabels(nint root)
    {
        var labels = new List<string>();
        EnumChildWindows(root, (hWnd, _) =>
        {
            var label = StripMnemonic(GetWindowText(hWnd));
            if (!string.IsNullOrWhiteSpace(label))
                labels.Add(label);
            return labels.Count < 24;
        }, 0);
        return labels;
    }

    static void CollectChildText(nint root, StringBuilder blob, int depth)
    {
        if (depth > 6 || blob.Length > 4000)
            return;

        EnumChildWindows(root, (hWnd, _) =>
        {
            var t = GetWindowText(hWnd);
            if (!string.IsNullOrWhiteSpace(t))
                blob.Append(t).Append(' ');
            if (depth < 4)
                CollectChildText(hWnd, blob, depth + 1);
            return blob.Length < 4000;
        }, 0);
    }

    static bool IsCursorLikeOwner(nint hWnd)
    {
        _ = GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0)
            return false;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName ?? "";
            return name.Contains("Cursor", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("Code", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Code - Insiders", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static string GetWindowText(nint hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0)
            return "";
        var sb = new StringBuilder(len + 1);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    static string GetClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
