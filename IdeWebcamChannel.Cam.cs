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
    static object Frame(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        // Inject workspace_path for Shared ToolArgs contract (capture-mcp parity).
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var merged = new Dictionary<string, JsonElement>(args, StringComparer.OrdinalIgnoreCase)
        {
            ["workspace_path"] = JsonSerializer.SerializeToElement(workspace)
        };

        var cameraIndex = GetOptionalInt(merged, "camera_index", 0);
        var warmupFrames = Math.Clamp(GetOptionalInt(merged, "warmup_frames", DefaultWarmupFrames), 0, 50);
        var requestedWidth = GetOptionalInt(merged, "width", 0);
        var requestedHeight = GetOptionalInt(merged, "height", 0);
        var jpegQuality = Math.Clamp(GetOptionalInt(merged, "jpeg_quality", DefaultJpegQuality), 1, 100);
        var imageFormat = NormalizeImageFormat(GetOptionalString(merged, "image_format") ?? "jpg");
        var outputSubdir = GetOptionalString(merged, "output_subdir") ?? DefaultOutputSubdir;
        var fileName = GetOptionalString(merged, "file_name") ?? GetOptionalString(merged, "name");

        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            Directory.CreateDirectory(workspaceRoot);

        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");

        Directory.CreateDirectory(outputDir);

        var safeBaseName = string.IsNullOrWhiteSpace(fileName)
            ? $"webcam-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(fileName);
        var outputPath = Path.Combine(outputDir, $"{safeBaseName}.{imageFormat}");

        using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
        if (!capture.IsOpened())
            throw new ArgumentException($"Camera {cameraIndex} is not available.");

        if (requestedWidth > 0)
            capture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
        if (requestedHeight > 0)
            capture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);

        using var frame = new Mat();
        for (var i = 0; i < warmupFrames; i++)
        {
            capture.Read(frame);
            Thread.Sleep(40);
        }

        if (!capture.Read(frame) || frame.Empty())
            throw new ArgumentException("Failed to read frame from webcam.");

        var writeOk = imageFormat switch
        {
            "jpg" => Cv2.ImWrite(outputPath, frame, [new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality)]),
            "png" => Cv2.ImWrite(outputPath, frame),
            _ => false
        };
        if (!writeOk)
            throw new ArgumentException("Failed to save captured frame.");

        return new
        {
            schema = Schema,
            ok = true,
            op = "frame",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · frame · {frame.Width}x{frame.Height}",
            success = true,
            file_path = outputPath,
            width = frame.Width,
            height = frame.Height,
            camera_index = cameraIndex,
            image_format = imageFormat,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "Read file_path (vision) or cdp_buffer op=take path= for agent image."
        };
    }

    /// <summary>Webcam burst via OpenCv VideoCapture — capture-mcp parity (frames only; no video writer in this slice).</summary>
    static object Burst(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            Directory.CreateDirectory(workspaceRoot);

        var cameraIndex = GetOptionalInt(args, "camera_index", 0);
        var warmupFrames = Math.Clamp(GetOptionalInt(args, "warmup_frames", DefaultWarmupFrames), 0, 50);
        var requestedWidth = GetOptionalInt(args, "width", 0);
        var requestedHeight = GetOptionalInt(args, "height", 0);
        var durationSec = Math.Clamp(GetOptionalInt(args, "duration_sec", DefaultBurstDurationSec), 1, 60);
        var targetFps = Math.Clamp(GetOptionalInt(args, "target_fps", DefaultBurstTargetFps), 1, 60);
        var jpegQuality = Math.Clamp(GetOptionalInt(args, "jpeg_quality", DefaultJpegQuality), 1, 100);
        var imageFormat = NormalizeImageFormat(GetOptionalString(args, "image_format") ?? "jpg");
        var outputSubdir = GetOptionalString(args, "output_subdir") ?? DefaultOutputSubdir;
        var burstName = GetOptionalString(args, "burst_name") ?? GetOptionalString(args, "name");

        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");
        Directory.CreateDirectory(outputDir);

        var safeBurstName = string.IsNullOrWhiteSpace(burstName)
            ? $"burst-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(burstName);
        var burstDir = Path.Combine(outputDir, safeBurstName);
        Directory.CreateDirectory(burstDir);

        using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
        if (!capture.IsOpened())
            throw new ArgumentException($"Camera {cameraIndex} is not available.");

        if (requestedWidth > 0)
            capture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
        if (requestedHeight > 0)
            capture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);

        using var frame = new Mat();
        for (var i = 0; i < warmupFrames; i++)
        {
            capture.Read(frame);
            Thread.Sleep(40);
        }

        var intervalMs = 1000.0 / targetFps;
        var durationMs = durationSec * 1000.0;
        var stopwatch = Stopwatch.StartNew();
        var nextCaptureAt = 0.0;
        var frameCount = 0;
        var firstFrameAtMs = -1.0;
        var lastFrameAtMs = -1.0;
        var frameWidth = 0;
        var frameHeight = 0;

        while (stopwatch.Elapsed.TotalMilliseconds <= durationMs)
        {
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            if (elapsed < nextCaptureAt)
            {
                var wait = Math.Max(1, (int)(nextCaptureAt - elapsed));
                Thread.Sleep(Math.Min(wait, 5));
                continue;
            }

            if (!capture.Read(frame) || frame.Empty())
                throw new ArgumentException("Failed to read frame from webcam during burst.");

            frameCount++;
            firstFrameAtMs = firstFrameAtMs < 0 ? elapsed : firstFrameAtMs;
            lastFrameAtMs = elapsed;
            frameWidth = frame.Width;
            frameHeight = frame.Height;

            var framePath = Path.Combine(burstDir, $"{frameCount:D5}.{imageFormat}");
            var writeOk = imageFormat switch
            {
                "jpg" => Cv2.ImWrite(framePath, frame, [new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality)]),
                "png" => Cv2.ImWrite(framePath, frame),
                _ => false
            };
            if (!writeOk)
                throw new ArgumentException($"Failed to save burst frame: {framePath}");

            nextCaptureAt += intervalMs;
        }

        if (frameCount == 0)
            throw new ArgumentException("No frames were captured from webcam.");

        var actualDurationMs = Math.Max(1.0, lastFrameAtMs - firstFrameAtMs);
        var actualFps = frameCount == 1 ? 1.0 : (frameCount - 1) * 1000.0 / actualDurationMs;

        return new
        {
            schema = Schema,
            ok = true,
            op = "burst",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · burst · {frameCount}f · {actualFps:F1}fps · {frameWidth}x{frameHeight}",
            success = true,
            burst_dir = burstDir,
            frames_captured = frameCount,
            target_fps = targetFps,
            actual_fps = Math.Round(actualFps, 2),
            duration_sec = durationSec,
            frame_width = frameWidth,
            frame_height = frameHeight,
            camera_index = cameraIndex,
            image_format = imageFormat,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "op=analyze|ocr burst_dir=<burst_dir> — in-proc"
        };
    }

}
