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

    /// <summary>Motion analyze on burst frames — analysis-mcp <c>analyze_burst_sequence</c> parity.</summary>
    static object Analyze(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            throw new ArgumentException($"Workspace directory does not exist: {workspaceRoot}");

        var burstDirInput = Opt(args, "burst_dir") ?? Opt(args, "images_dir") ?? Opt(args, "dir")
            ?? throw new ArgumentException("burst_dir is required (folder of frames).");
        var burstDir = Path.IsPathRooted(burstDirInput)
            ? Path.GetFullPath(burstDirInput)
            : Path.GetFullPath(Path.Combine(workspaceRoot, burstDirInput));
        if (!burstDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("burst_dir points outside of workspace_path.");
        if (!Directory.Exists(burstDir))
            throw new ArgumentException($"Burst directory does not exist: {burstDir}");

        var sampleEvery = Math.Clamp(GetOptionalInt(args, "sample_every", 1), 1, 100);
        var maxFrames = Math.Clamp(GetOptionalInt(args, "max_frames", 3000), 2, 10000);
        var sceneCutThreshold = Math.Clamp(GetOptionalDouble(args, "scene_cut_threshold", 35), 1, 255);

        var allFrames = Directory
            .EnumerateFiles(burstDir)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allFrames.Count == 0)
            throw new ArgumentException("No image files found in burst_dir.");

        var sampledFrames = allFrames
            .Where((_, index) => index % sampleEvery == 0)
            .Take(maxFrames)
            .ToList();
        if (sampledFrames.Count < 2)
            sampledFrames = [allFrames[0], allFrames[^1]];

        var timeline = new List<object>();
        var peaks = new List<(int Index, string Frame, double Score, string Level)>();
        var sumMotion = 0.0;
        var maxMotion = double.MinValue;
        var minMotion = double.MaxValue;
        var pairCount = 0;

        using var prev = Cv2.ImRead(sampledFrames[0], ImreadModes.Grayscale);
        if (prev.Empty())
            throw new ArgumentException($"Failed to read frame: {sampledFrames[0]}");

        using var diff = new Mat();
        using var previous = prev.Clone();
        var previousFile = sampledFrames[0];

        for (var i = 1; i < sampledFrames.Count; i++)
        {
            var currentFile = sampledFrames[i];
            using var current = Cv2.ImRead(currentFile, ImreadModes.Grayscale);
            if (current.Empty())
                continue;

            if (current.Size() != previous.Size())
                Cv2.Resize(current, current, previous.Size());

            Cv2.Absdiff(previous, current, diff);
            var motionScore = Cv2.Mean(diff).Val0;
            var level = ClassifyMotion(motionScore);
            var isCut = motionScore >= sceneCutThreshold;

            timeline.Add(new
            {
                from = Path.GetFileName(previousFile),
                to = Path.GetFileName(currentFile),
                motion_score = Math.Round(motionScore, 2),
                motion_level = level,
                is_scene_cut = isCut
            });

            sumMotion += motionScore;
            maxMotion = Math.Max(maxMotion, motionScore);
            minMotion = Math.Min(minMotion, motionScore);
            peaks.Add((i, Path.GetFileName(currentFile), motionScore, level));
            pairCount++;

            current.CopyTo(previous);
            previousFile = currentFile;
        }

        if (pairCount == 0)
            throw new ArgumentException("Unable to analyze burst frames (no valid frame pairs).");

        var avgMotion = sumMotion / pairCount;
        var topPeaks = peaks
            .OrderByDescending(p => p.Score)
            .Take(5)
            .Select(p => new { frame = p.Frame, motion_score = Math.Round(p.Score, 2), motion_level = p.Level })
            .ToList();
        var sceneCuts = timeline
            .Where(t =>
            {
                var el = JsonSerializer.SerializeToElement(t);
                return el.TryGetProperty("is_scene_cut", out var c) && c.ValueKind == JsonValueKind.True;
            })
            .ToList();

        var summary =
            $"Analyzed {sampledFrames.Count} sampled frames from {allFrames.Count} total. " +
            $"Motion avg={avgMotion:F2}, min={minMotion:F2}, max={maxMotion:F2}. " +
            $"Scene cuts detected: {sceneCuts.Count}.";

        return new
        {
            schema = Schema,
            ok = true,
            op = "analyze",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · analyze · avg={avgMotion:F1} · cuts={sceneCuts.Count}",
            success = true,
            burst_dir = burstDir,
            total_frames = allFrames.Count,
            sampled_frames = sampledFrames.Count,
            sample_every = sampleEvery,
            avg_motion_score = Math.Round(avgMotion, 2),
            min_motion_score = Math.Round(minMotion, 2),
            max_motion_score = Math.Round(maxMotion, 2),
            scene_cut_threshold = Math.Round(sceneCutThreshold, 2),
            scene_cut_count = sceneCuts.Count,
            top_motion_peaks = topPeaks,
            scene_cuts = sceneCuts,
            timeline,
            summary,
            analyzed_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "timeline[] + top_motion_peaks — motion between frames"
        };
    }

}
