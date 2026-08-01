#nullable enable
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Cdp.Core;
using static WebcamMcp.Shared.McpDefaults;
using static WebcamMcp.Shared.ToolArgs;

namespace CdpMcp;

internal static partial class IdeWebcamChannel
{
    const uint PwRenderFullContent = 0x00000002;

    /// <summary>
    /// HWND capture via PrintWindow (not full virtual screen).
    /// op=window_list|windows · op=window|window_snap|capture_window.
    /// </summary>
    static object Window(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var opRaw = (Opt(args, "op") ?? "window").Trim().ToLowerInvariant();
        var listOnly = opRaw is "window_list" or "windows" or "list_windows";

        var windows = EnumerateTopLevelWindows();
        var processFilter = Opt(args, "process") ?? Opt(args, "process_name") ?? Opt(args, "exe");
        var titleFilter = Opt(args, "title") ?? Opt(args, "title_contains") ?? Opt(args, "name");
        var hwndArg = Opt(args, "hwnd") ?? Opt(args, "handle");
        var max = Math.Clamp(GetOptionalInt(args, "max", 40), 1, 200);

        if (!string.IsNullOrWhiteSpace(processFilter))
        {
            windows = windows
                .Where(w => w.ProcessName.Contains(processFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                            || string.Equals(w.ProcessName, processFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(titleFilter))
        {
            windows = windows
                .Where(w => w.Title.Contains(titleFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(hwndArg) && TryParseHwnd(hwndArg, out var hwndFilter))
            windows = windows.Where(w => w.Hwnd == hwndFilter).ToList();

        if (listOnly)
        {
            var listed = windows.Take(max).Select(ToWire).ToList();
            return new
            {
                schema = Schema,
                ok = true,
                op = "window_list",
                go = GoName,
                tool = ToolName,
                pulse = $"webcam · windows · {listed.Count}/{windows.Count}",
                count = listed.Count,
                total_matched = windows.Count,
                windows = listed,
                hint = "op=window hwnd=|process=|title= → PNG of that HWND (PrintWindow)"
            };
        }

        if (windows.Count == 0)
            throw new ArgumentException("No matching visible top-level window. Try op=window_list.");

        if (windows.Count > 1
            && string.IsNullOrWhiteSpace(hwndArg)
            && string.IsNullOrWhiteSpace(titleFilter)
            && string.IsNullOrWhiteSpace(processFilter))
        {
            return new
            {
                schema = Schema,
                ok = false,
                op = "window",
                error = "ambiguous",
                detail = "Multiple windows — pass hwnd= / process= / title=, or op=window_list.",
                go = GoName,
                tool = ToolName,
                count = Math.Min(windows.Count, max),
                windows = windows.Take(max).Select(ToWire).ToList()
            };
        }

        // Prefer largest area among matches when process/title hit several (e.g. Glass zone hosts).
        var target = windows
            .OrderByDescending(w => (long)w.Width * w.Height)
            .First();

        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        Directory.CreateDirectory(workspaceRoot);

        var outputSubdir = Opt(args, "output_subdir") ?? ".cascade-ide/window-captures";
        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");
        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");
        Directory.CreateDirectory(outputDir);

        var imageFormat = NormalizeImageFormat(GetOptionalString(args, "image_format") ?? "png");
        var jpegQuality = Math.Clamp(GetOptionalInt(args, "jpeg_quality", DefaultJpegQuality), 1, 100);
        var fileName = Opt(args, "file_name") ?? Opt(args, "burst_name");
        var safeName = string.IsNullOrWhiteSpace(fileName)
            ? $"window-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(fileName);
        var filePath = Path.Combine(outputDir, $"{safeName}.{imageFormat}");

        using var bitmap = CaptureHwndBitmap(target.Hwnd, target.Width, target.Height);
        SaveBitmap(bitmap, filePath, imageFormat, jpegQuality);

        return new
        {
            schema = Schema,
            ok = true,
            op = "window",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · window · {target.ProcessName} · {target.Width}x{target.Height}",
            success = true,
            file_path = filePath,
            hwnd = unchecked((long)target.Hwnd),
            process_id = target.ProcessId,
            process_name = target.ProcessName,
            title = target.Title,
            width = target.Width,
            height = target.Height,
            image_format = imageFormat,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            method = "PrintWindow",
            hint = "Read PNG path; op=ocr file_path= for text"
        };
    }

    static object ToWire(WinInfo w) => new
    {
        hwnd = unchecked((long)w.Hwnd),
        process_id = w.ProcessId,
        process_name = w.ProcessName,
        title = w.Title,
        width = w.Width,
        height = w.Height,
        x = w.X,
        y = w.Y
    };

    static Bitmap CaptureHwndBitmap(IntPtr hwnd, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            if (!NativePrintWindow(hwnd, hdc, PwRenderFullContent))
            {
                // Fallback: BitBlt from window DC (may be blank for some GPU paths).
                var windowDc = NativeGetWindowDC(hwnd);
                try
                {
                    if (windowDc == IntPtr.Zero
                        || !NativeBitBlt(hdc, 0, 0, width, height, windowDc, 0, 0, 0x00CC0020 /* SRCCOPY */))
                    {
                        throw new InvalidOperationException(
                            $"PrintWindow/BitBlt failed for hwnd={unchecked((long)hwnd)}.");
                    }
                }
                finally
                {
                    if (windowDc != IntPtr.Zero)
                        _ = NativeReleaseDC(hwnd, windowDc);
                }
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        return bitmap;
    }

    static List<WinInfo> EnumerateTopLevelWindows()
    {
        var list = new List<WinInfo>();
        NativeEnumWindows((hWnd, _lParam) =>
        {
            if (!NativeIsWindowVisible(hWnd))
                return true;
            if (NativeGetWindow(hWnd, GwOwner) != IntPtr.Zero)
                return true;

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            if (!NativeGetWindowRect(hWnd, out var rect))
                return true;
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w < 80 || h < 40)
                return true;

            uint pid = 0;
            NativeGetWindowThreadProcessId(hWnd, out pid);
            var processName = "?";
            try
            {
                using var p = Process.GetProcessById(unchecked((int)pid));
                processName = p.ProcessName;
            }
            catch
            {
                /* gone */
            }

            list.Add(new WinInfo(hWnd, pid, processName, title, rect.Left, rect.Top, w, h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string GetWindowTitle(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0)
            return "";
        var sb = new StringBuilder(len + 1);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    static bool TryParseHwnd(string raw, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var hex))
            {
                hwnd = new IntPtr(hex);
                return true;
            }

            return false;
        }

        if (long.TryParse(s, out var n))
        {
            hwnd = new IntPtr(n);
            return true;
        }

        return false;
    }

    readonly record struct WinInfo(
        IntPtr Hwnd,
        uint ProcessId,
        string ProcessName,
        string Title,
        int X,
        int Y,
        int Width,
        int Height);

    const uint GwOwner = 4;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    static extern bool NativeEnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    static extern bool NativeIsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    static extern bool NativeGetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    static extern uint NativeGetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    static extern IntPtr NativeGetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "PrintWindow")]
    static extern bool NativePrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowDC")]
    static extern IntPtr NativeGetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    static extern int NativeReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
    static extern bool NativeBitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
