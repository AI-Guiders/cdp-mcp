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


    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

}
