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
            return TryClickKeepWaitingWindows();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ide_ignite] stall-dialog probe failed: {ex.Message}");
            return false;
        }
    }

    static bool TryClickKeepWaitingWindows()
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
            if (!LooksLikeStallMessage(text) && !text.Contains("not responding", StringComparison.OrdinalIgnoreCase))
                return true;

            // Prefer Cursor/Code process; still allow if message is exact VS Code stall copy.
            if (!IsCursorLikeOwner(hWnd) && !LooksLikeStallMessage(text))
                return true;

            if (!TryFindKeepWaitingButton(hWnd, out var button))
                return true;

            hits.Add(button);
            return false; // stop enum — one dialog enough
        }, 0);

        if (hits.Count == 0)
            return false;

        var btn = hits[0];
        // BM_CLICK synthesizes mouse down/up on the button.
        _ = SendMessage(btn, BmClick, 0, 0);
        return true;
    }

    static bool TryFindKeepWaitingButton(nint root, out nint button)
    {
        nint found = 0;
        EnumChildWindows(root, (hWnd, _) =>
        {
            var cls = GetClassName(hWnd);
            if (!cls.Contains("Button", StringComparison.OrdinalIgnoreCase)
                && !cls.Equals("Button", StringComparison.OrdinalIgnoreCase))
            {
                // Chromium/Electron may use custom classes — still check label.
            }

            var label = GetWindowText(hWnd);
            if (IsKeepWaitingLabel(label))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, 0);

        button = found;
        return found != 0;
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
