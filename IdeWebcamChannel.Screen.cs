#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Cdp.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenCvSharp;
using WebcamMcp.Shared;
using Whisper.net;
using static WebcamMcp.Shared.McpDefaults;
using static WebcamMcp.Shared.MotionAnalysis;
using static WebcamMcp.Shared.ToolArgs;

namespace CdpMcp;

internal static partial class IdeWebcamChannel
{
    /// <summary>Screen burst via GDI CopyFromScreen — capture-mcp parity (no video in this slice).</summary>
    static object Screen(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            Directory.CreateDirectory(workspaceRoot);

        var durationSec = Math.Clamp(GetOptionalInt(args, "duration_sec", 1), 1, 60);
        var targetFps = Math.Clamp(GetOptionalInt(args, "target_fps", 2), 1, 60);
        var jpegQuality = Math.Clamp(GetOptionalInt(args, "jpeg_quality", DefaultJpegQuality), 1, 100);
        var imageFormat = NormalizeImageFormat(GetOptionalString(args, "image_format") ?? "jpg");
        var outputSubdir = GetOptionalString(args, "output_subdir") ?? ".cascade-ide/screen-captures";
        var burstName = GetOptionalString(args, "burst_name") ?? GetOptionalString(args, "name");

        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");
        Directory.CreateDirectory(outputDir);

        var safeBurstName = string.IsNullOrWhiteSpace(burstName)
            ? $"screen-burst-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(burstName);
        var burstDir = Path.Combine(outputDir, safeBurstName);
        Directory.CreateDirectory(burstDir);

        var hasExplicitRegion = args.ContainsKey("x") || args.ContainsKey("y")
            || args.ContainsKey("width") || args.ContainsKey("height");
        var (vx, vy, vw, vh) = VirtualScreenBounds();
        var captureX = GetOptionalInt(args, "x", vx);
        var captureY = GetOptionalInt(args, "y", vy);
        var captureWidth = Math.Max(1, GetOptionalInt(args, "width", hasExplicitRegion ? vw : vw));
        var captureHeight = Math.Max(1, GetOptionalInt(args, "height", hasExplicitRegion ? vh : vh));
        if (!hasExplicitRegion)
        {
            captureX = vx;
            captureY = vy;
            captureWidth = Math.Max(1, vw);
            captureHeight = Math.Max(1, vh);
        }

        var intervalMs = 1000.0 / targetFps;
        var durationMs = durationSec * 1000.0;
        var stopwatch = Stopwatch.StartNew();
        var nextCaptureAt = 0.0;
        var frameCount = 0;
        var firstFrameAtMs = -1.0;
        var lastFrameAtMs = -1.0;

        while (stopwatch.Elapsed.TotalMilliseconds <= durationMs)
        {
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            if (elapsed < nextCaptureAt)
            {
                var wait = Math.Max(1, (int)(nextCaptureAt - elapsed));
                Thread.Sleep(Math.Min(wait, 5));
                continue;
            }

            using var bitmap = new System.Drawing.Bitmap(
                captureWidth,
                captureHeight,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    captureX,
                    captureY,
                    0,
                    0,
                    new System.Drawing.Size(captureWidth, captureHeight),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            frameCount++;
            firstFrameAtMs = firstFrameAtMs < 0 ? elapsed : firstFrameAtMs;
            lastFrameAtMs = elapsed;

            var framePath = Path.Combine(burstDir, $"{frameCount:D5}.{imageFormat}");
            SaveBitmap(bitmap, framePath, imageFormat, jpegQuality);
            nextCaptureAt += intervalMs;
        }

        if (frameCount == 0)
            throw new ArgumentException("No frames were captured from screen.");

        var actualDurationMs = Math.Max(1.0, lastFrameAtMs - firstFrameAtMs);
        var actualFps = frameCount == 1 ? 1.0 : (frameCount - 1) * 1000.0 / actualDurationMs;

        return new
        {
            schema = Schema,
            ok = true,
            op = "screen",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · screen · {frameCount}f · {actualFps:F1}fps",
            success = true,
            burst_dir = burstDir,
            frames_captured = frameCount,
            target_fps = targetFps,
            actual_fps = Math.Round(actualFps, 2),
            duration_sec = durationSec,
            capture_region = new { x = captureX, y = captureY, width = captureWidth, height = captureHeight },
            image_format = imageFormat,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "op=ocr images_dir=<burst_dir relative or absolute under workspace>"
        };
    }


}
