#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CdpMcp;

/// <summary>Win32 enum/click peel for native Cursor dialogs (≤ADX soft-warn).</summary>
internal static partial class IdeIgniteNativeDialogs
{
    static bool TryClickLabeledButtonWindows(
        Func<string?, bool> looksLikeDialog,
        Func<string?, bool> isButtonLabel,
        string logTag,
        Func<IReadOnlyList<string>, bool>? buttonShapeFallback)
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
            // DirectUI/Chromium often leaves WM_GETTEXT empty — OOM body lives in MSAA.
            CollectAccessibleNamesInto(hWnd, blob);

            var text = blob.ToString();
            var labels = CollectButtonLabels(hWnd);
            var textMatch = looksLikeDialog(text)
                            || (logTag == "stall-dialog"
                                && text.Contains("not responding", StringComparison.OrdinalIgnoreCase));
            var cursorOwned = IsCursorLikeOwner(hWnd);
            var electronish = IsElectronLikeClass(hWnd);
            // During OOM the owner process may already be dead — GetProcessById fails.
            // Still accept Reopen+Close shape on Electron class (dogfood 2026-08-01 stream-OOM miss).
            var shapeMatch = buttonShapeFallback is not null
                             && (cursorOwned || electronish)
                             && buttonShapeFallback(labels);

            if (!textMatch && !shapeMatch)
                return true;

            if (!cursorOwned && !electronish && !textMatch)
                return true;

            if (TryFindButtonByLabel(hWnd, isButtonLabel, out var button))
            {
                ClickButton(button);
                hits.Add(button);
                return false;
            }

            if (TryClickAccessibleByLabel(hWnd, isButtonLabel))
            {
                hits.Add(hWnd);
                return false;
            }

            LogMiss(logTag, text, labels, shapeMatch);
            return true;
        }, 0);

        return hits.Count > 0;
    }

    static void LogMiss(string logTag, string text, List<string> labels, bool shapeMatch)
    {
        var preview = text.Length <= 240 ? text : text[..240];
        var line =
            $"[ide_ignite] {logTag} matched {(shapeMatch ? "button-shape" : "text")} but no recovery button; labels=[{string.Join(" | ", labels)}] text=[{preview}]";
        Console.Error.WriteLine(line);
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cdp-mcp");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, $"{logTag}-LATEST.txt"),
                $"{DateTimeOffset.UtcNow:O}\n{line}\n");
        }
        catch
        {
            /* best-effort */
        }
    }

    static void ClickButton(nint button)
    {
        _ = SendMessage(button, BmClick, 0, 0);
        _ = PostMessage(button, BmClick, 0, 0);
    }

    static bool TryFindButtonByLabel(nint root, Func<string?, bool> isLabel, out nint button)
    {
        nint found = 0;
        _ = WalkChildren(root, depth: 0, (hWnd, _) =>
        {
            if (found != 0)
                return false;
            if (!isLabel(GetWindowText(hWnd)))
                return true;
            found = hWnd;
            return false;
        });

        button = found;
        return found != 0;
    }

    static List<string> CollectButtonLabels(nint root)
    {
        var labels = new List<string>();
        _ = WalkChildren(root, depth: 0, (hWnd, _) =>
        {
            var label = StripMnemonic(GetWindowText(hWnd));
            if (!string.IsNullOrWhiteSpace(label)
                && !labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                labels.Add(label);
            return labels.Count < 48;
        });

        CollectAccessibleNames(root, labels);
        return labels;
    }

    /// <returns>false = abort further siblings.</returns>
    static bool WalkChildren(nint root, int depth, Func<nint, int, bool> visit)
    {
        if (depth > 8)
            return true;

        var cont = true;
        EnumChildWindows(root, (hWnd, _) =>
        {
            if (!visit(hWnd, depth))
            {
                cont = false;
                return false;
            }

            if (!WalkChildren(hWnd, depth + 1, visit))
            {
                cont = false;
                return false;
            }

            return true;
        }, 0);
        return cont;
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

    /// <summary>
    /// Electron/Chromium top-level (Cursor dialog). Used when owner PID is already dead after OOM.
    /// </summary>
    internal static bool LooksLikeElectronClassName(string? cls)
    {
        if (string.IsNullOrWhiteSpace(cls))
            return false;
        return cls.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase)
               || cls.Equals("#32770", StringComparison.OrdinalIgnoreCase)
               || cls.Contains("Electron", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsElectronLikeClass(nint hWnd) =>
        LooksLikeElectronClassName(GetClassName(hWnd));

    static string GetClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
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

    [DllImport("user32.dll")]
    static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);
}
