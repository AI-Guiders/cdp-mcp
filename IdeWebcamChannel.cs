#nullable enable
using System.Diagnostics;
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

/// <summary>
/// Soft organ <c>go=webcam_desk</c> / Meta <c>cdp_webcam</c> — in-proc sense plane via
/// <c>AIGuiders.WebcamMcp.Shared</c> + OpenCv (not parked Cursor webcam-mcp guest).
/// Capture + OCR aligned with webcam-*-mcp split; more analysis later.
/// </summary>
internal static class IdeWebcamChannel
{
    public const string Schema = "webcam/v0";
    public const string ToolName = "cdp_webcam";
    public const string GoName = "webcam_desk";

    /// <summary>Env override for tesseract.exe (analysis-mcp parity).</summary>
    const string TesseractEnvKey = "WEBCAM_MCP_TESSERACT_PATH";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static string HandleJson(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args) =>
        JsonSerializer.Serialize(Handle(session, args), Pretty);

    public static object Handle(
        SessionContext session,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        args ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var op = (Opt(args, "op") ?? Opt(args, "cmd") ?? "scene").Trim().ToLowerInvariant();
        try
        {
            return op switch
            {
                "scene" or "status" or "caps" => Scene(session),
                "frame" or "snap" or "capture" or "photo" => Frame(session, args),
                "burst" or "webcam_burst" or "capture_burst" => Burst(session, args),
                "av" or "av_burst" or "capture_av" or "capture_av_burst" => Av(session, args),
                "screen" or "screen_burst" or "capture_screen_burst" => Screen(session, args),
                "audio" or "record_audio" or "capture_audio" => Audio(session, args),
                "transcribe" or "transcribe_audio" or "transcribe_audio_whisper" or "whisper" =>
                    Transcribe(session, args),
                "ocr" or "ocr_batch" or "ocr_image_batch" => Ocr(session, args),
                "analyze" or "analyze_burst" or "analyze_burst_sequence" => Analyze(session, args),
                _ => Scene(session)
            };
        }
        catch (Exception ex)
        {
            return new
            {
                schema = Schema,
                ok = false,
                op,
                error = "exception",
                detail = ex.Message,
                go = GoName,
                tool = ToolName
            };
        }
    }

    static object Scene(SessionContext session)
    {
        var root = ResolveWorkspace(session, null);
        return new
        {
            schema = Schema,
            ok = true,
            op = "scene",
            go = GoName,
            tool = ToolName,
            pulse = "webcam · in-proc · frame|burst|av|screen|audio|transcribe|ocr|analyze",
            workspace = root,
            core = "AIGuiders.WebcamMcp.Shared 0.1.0",
            ops = new[] { "scene", "frame", "burst", "av", "screen", "audio", "transcribe", "ocr", "analyze" },
            planned = Array.Empty<string>(),
            audio = AudioDeviceScene(),
            whisper_model = Environment.GetEnvironmentVariable(EnvWhisperModelPath),
            hint = "op=av → frames+audio(+video); op=transcribe audio_path= — capture/analysis parity"
        };
    }

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

    /// <summary>Webcam + mic concurrent session — capture-mcp <c>capture_av_burst</c> parity.</summary>
    static object Av(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            Directory.CreateDirectory(workspaceRoot);

        var durationSec = Math.Clamp(GetOptionalInt(args, "duration_sec", DefaultAudioDurationSec), 1, 60);
        var targetFps = Math.Clamp(GetOptionalInt(args, "target_fps", DefaultBurstTargetFps), 1, 60);
        var cameraIndex = GetOptionalInt(args, "camera_index", 0);
        var audioDeviceNumber = Math.Clamp(
            GetOptionalInt(args, "device_number", GetOptionalInt(args, "audio_device_number", 0)),
            0,
            32);
        var requestedWidth = GetOptionalInt(args, "width", 0);
        var requestedHeight = GetOptionalInt(args, "height", 0);
        var audioSampleRate = Math.Clamp(
            GetOptionalInt(args, "sample_rate", GetOptionalInt(args, "audio_sample_rate", DefaultAudioSampleRate)),
            8000,
            96000);
        var audioChannels = Math.Clamp(
            GetOptionalInt(args, "channels", GetOptionalInt(args, "audio_channels", DefaultAudioChannels)),
            1,
            2);
        var warmupFrames = Math.Clamp(GetOptionalInt(args, "warmup_frames", DefaultWarmupFrames), 0, 50);
        var imageFormat = NormalizeImageFormat(GetOptionalString(args, "image_format") ?? "jpg");
        var jpegQuality = Math.Clamp(GetOptionalInt(args, "jpeg_quality", DefaultJpegQuality), 1, 100);
        var outputSubdir = GetOptionalString(args, "output_subdir") ?? DefaultAvOutputSubdir;
        var sessionName = GetOptionalString(args, "session_name")
            ?? GetOptionalString(args, "burst_name")
            ?? GetOptionalString(args, "name");
        var saveVideo = GetOptionalBool(args, "save_video", true);
        var videoFps = Math.Clamp(GetOptionalInt(args, "video_fps", DefaultBurstVideoFps), 1, 60);

        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");
        Directory.CreateDirectory(outputDir);

        var safeSessionName = string.IsNullOrWhiteSpace(sessionName)
            ? $"av-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(sessionName);
        var sessionDir = Path.Combine(outputDir, safeSessionName);
        var framesDir = Path.Combine(sessionDir, "frames");
        Directory.CreateDirectory(framesDir);

        var audioPath = Path.Combine(sessionDir, "audio.wav");
        var metadataPath = Path.Combine(sessionDir, "metadata.json");
        var videoPath = saveVideo ? Path.Combine(sessionDir, "video.mp4") : null;

        if (WaveInEvent.DeviceCount == 0)
            throw new ArgumentException("No recording devices were found.");
        if (audioDeviceNumber >= WaveInEvent.DeviceCount)
            throw new ArgumentException(
                $"device_number {audioDeviceNumber} is out of range. Available devices: {WaveInEvent.DeviceCount}.");

        using var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
        if (!capture.IsOpened())
            throw new ArgumentException($"Camera {cameraIndex} is not available.");
        if (requestedWidth > 0)
            capture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
        if (requestedHeight > 0)
            capture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = audioDeviceNumber,
            WaveFormat = new WaveFormat(audioSampleRate, 16, audioChannels),
            BufferMilliseconds = 50
        };
        using var audioCompleted = new ManualResetEventSlim(false);
        Exception? audioError = null;
        var audioLock = new object();
        var frameTimestampsMs = new List<int>();
        var frameCount = 0;
        var startUtc = DateTime.UtcNow;
        var durationMs = durationSec * 1000.0;
        var intervalMs = 1000.0 / targetFps;
        var stopwatch = Stopwatch.StartNew();
        var nextCaptureAt = 0.0;

        // WaveFileWriter finalizes WAV header on Dispose — must close before length check (capture parity).
        {
            using var audioWriter = new WaveFileWriter(audioPath, waveIn.WaveFormat);
            waveIn.DataAvailable += (_, eventArgs) =>
            {
                lock (audioLock)
                {
                    audioWriter.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                    audioWriter.Flush();
                }
            };
            waveIn.RecordingStopped += (_, eventArgs) =>
            {
                audioError = eventArgs.Exception;
                audioCompleted.Set();
            };

            using var frame = new Mat();
            VideoWriter? videoWriter = null;
            try
            {
                for (var i = 0; i < warmupFrames; i++)
                {
                    capture.Read(frame);
                    Thread.Sleep(15);
                }

                waveIn.StartRecording();

                while (stopwatch.Elapsed.TotalMilliseconds <= durationMs)
                {
                    var elapsed = stopwatch.Elapsed.TotalMilliseconds;
                    if (elapsed < nextCaptureAt)
                    {
                        var waitMs = Math.Max(1, (int)(nextCaptureAt - elapsed));
                        Thread.Sleep(Math.Min(waitMs, 5));
                        continue;
                    }

                    if (!capture.Read(frame) || frame.Empty())
                    {
                        nextCaptureAt += intervalMs;
                        continue;
                    }

                    frameCount++;
                    var framePath = Path.Combine(framesDir, $"{frameCount:D5}.{imageFormat}");
                    var saved = imageFormat switch
                    {
                        "jpg" => Cv2.ImWrite(framePath, frame, [new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality)]),
                        "png" => Cv2.ImWrite(framePath, frame),
                        _ => false
                    };
                    if (!saved)
                        throw new ArgumentException($"Failed to save video frame: {framePath}");

                    if (saveVideo)
                    {
                        if (videoWriter is null)
                        {
                            videoWriter = new VideoWriter(
                                videoPath!,
                                VideoWriter.FourCC('m', 'p', '4', 'v'),
                                videoFps,
                                new Size(frame.Width, frame.Height));
                            if (!videoWriter.IsOpened())
                            {
                                videoWriter.Dispose();
                                videoWriter = null;
                                throw new ArgumentException("Failed to initialize MP4 writer for A/V capture.");
                            }
                        }

                        videoWriter.Write(frame);
                    }

                    frameTimestampsMs.Add((int)Math.Round(elapsed));
                    nextCaptureAt += intervalMs;
                }
            }
            finally
            {
                waveIn.StopRecording();
                videoWriter?.Release();
                videoWriter?.Dispose();
            }

            if (!audioCompleted.Wait(TimeSpan.FromSeconds(8)))
                throw new ArgumentException("Timeout while finalizing audio recording.");
            if (audioError is not null)
                throw new ArgumentException("Audio capture failed: " + audioError.Message);
        }

        var audioInfo = new FileInfo(audioPath);
        if (!audioInfo.Exists || audioInfo.Length <= 44)
            throw new ArgumentException("A/V capture produced empty audio track.");
        if (frameCount == 0)
            throw new ArgumentException("A/V capture produced no video frames.");

        var actualDurationMs = stopwatch.Elapsed.TotalMilliseconds;
        var actualFps = actualDurationMs > 0 ? frameCount * 1000.0 / actualDurationMs : 0;
        var metadata = new
        {
            session_dir = sessionDir,
            start_utc = startUtc.ToString("O"),
            requested_duration_sec = durationSec,
            actual_duration_ms = (int)Math.Round(actualDurationMs),
            camera_index = cameraIndex,
            audio_device_number = audioDeviceNumber,
            frame_count = frameCount,
            frame_timestamps_ms = frameTimestampsMs,
            target_fps = targetFps,
            actual_fps = Math.Round(actualFps, 2),
            audio_path = audioPath,
            video_path = videoPath
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, Pretty));

        return new
        {
            schema = Schema,
            ok = true,
            op = "av",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · av · {frameCount}f · {actualFps:F1}fps · audio {audioInfo.Length}B",
            success = true,
            session_dir = sessionDir,
            frames_dir = framesDir,
            audio_path = audioPath,
            video_path = videoPath,
            metadata_path = metadataPath,
            frame_count = frameCount,
            actual_fps = Math.Round(actualFps, 2),
            duration_sec = durationSec,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "op=analyze burst_dir=frames_dir | op=transcribe audio_path="
        };
    }

    /// <summary>Mic WAV via NAudio WaveInEvent — capture-mcp <c>capture_audio</c> parity.</summary>
    static object Audio(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            Directory.CreateDirectory(workspaceRoot);

        var durationSec = Math.Clamp(GetOptionalInt(args, "duration_sec", DefaultAudioDurationSec), 1, 60);
        var sampleRate = Math.Clamp(
            GetOptionalInt(args, "sample_rate", GetOptionalInt(args, "audio_sample_rate", DefaultAudioSampleRate)),
            8000,
            96000);
        var channels = Math.Clamp(
            GetOptionalInt(args, "channels", GetOptionalInt(args, "audio_channels", DefaultAudioChannels)),
            1,
            2);
        var deviceNumber = Math.Clamp(
            GetOptionalInt(args, "device_number", GetOptionalInt(args, "audio_device_number", 0)),
            0,
            32);
        var outputSubdir = GetOptionalString(args, "output_subdir") ?? DefaultAudioOutputSubdir;
        var fileName = GetOptionalString(args, "file_name") ?? GetOptionalString(args, "name");

        if (Path.IsPathRooted(outputSubdir))
            throw new ArgumentException("output_subdir must be relative to workspace_path.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, outputSubdir));
        if (!outputDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_subdir points outside of workspace_path.");
        Directory.CreateDirectory(outputDir);

        var safeBaseName = string.IsNullOrWhiteSpace(fileName)
            ? $"audio-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}"
            : MakeSafeFileName(fileName);
        var outputPath = Path.Combine(outputDir, $"{safeBaseName}.wav");

        if (WaveInEvent.DeviceCount == 0)
            throw new ArgumentException("No recording devices were found.");
        if (deviceNumber >= WaveInEvent.DeviceCount)
            throw new ArgumentException(
                $"device_number {deviceNumber} is out of range. Available devices: {WaveInEvent.DeviceCount}.");

        using var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(sampleRate, 16, channels),
            BufferMilliseconds = 50
        };
        using var writer = new WaveFileWriter(outputPath, waveIn.WaveFormat);
        using var completed = new ManualResetEventSlim(false);
        Exception? recordingError = null;

        waveIn.DataAvailable += (_, eventArgs) =>
        {
            writer.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            writer.Flush();
        };
        waveIn.RecordingStopped += (_, eventArgs) =>
        {
            recordingError = eventArgs.Exception;
            completed.Set();
        };

        waveIn.StartRecording();
        Thread.Sleep(durationSec * 1000);
        waveIn.StopRecording();

        if (!completed.Wait(TimeSpan.FromSeconds(5)))
            throw new ArgumentException("Timeout while finalizing audio recording.");
        if (recordingError is not null)
            throw new ArgumentException("Audio capture failed: " + recordingError.Message);

        var fileInfo = new FileInfo(outputPath);
        if (!fileInfo.Exists || fileInfo.Length <= 44)
            throw new ArgumentException("Recorded file is empty.");

        return new
        {
            schema = Schema,
            ok = true,
            op = "audio",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · audio · wavein · {durationSec}s · {sampleRate}Hz · {channels}ch · {fileInfo.Length}B",
            success = true,
            file_path = outputPath,
            duration_sec = durationSec,
            sample_rate = sampleRate,
            channels,
            device_number = deviceNumber,
            bytes = fileInfo.Length,
            captured_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "Next: op=transcribe file_path="
        };
    }

    /// <summary>Whisper transcription — analysis-mcp <c>transcribe_audio_whisper</c> parity.</summary>
    static object Transcribe(SessionContext session, IReadOnlyDictionary<string, JsonElement> args) =>
        TranscribeAsync(session, args).GetAwaiter().GetResult();

    static async Task<object> TranscribeAsync(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            throw new ArgumentException($"Workspace directory does not exist: {workspaceRoot}");

        var audioPathInput = Opt(args, "audio_path") ?? Opt(args, "file_path") ?? Opt(args, "path")
            ?? throw new ArgumentException("audio_path / file_path is required.");
        var language = (Opt(args, "language") ?? Opt(args, "lang") ?? "auto").Trim().ToLowerInvariant();
        // OCR also uses lang= — if caller passed eng/rus tess style, treat as auto for whisper.
        if (language is "eng" or "rus" or "eng+rus" or "rus+eng")
            language = "auto";
        var maxSegments = Math.Clamp(GetOptionalInt(args, "max_segments", 1000), 1, 5000);

        var audioPath = Path.IsPathRooted(audioPathInput)
            ? Path.GetFullPath(audioPathInput)
            : Path.GetFullPath(Path.Combine(workspaceRoot, audioPathInput));
        if (!audioPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("audio_path points outside of workspace_path.");
        if (!File.Exists(audioPath))
            throw new ArgumentException($"Audio file does not exist: {audioPath}");

        var modelPath = Opt(args, "model_path") ?? Environment.GetEnvironmentVariable(EnvWhisperModelPath);
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("model_path is required or set WHISPER_MODEL_PATH env var.");
        modelPath = Path.GetFullPath(modelPath.Trim());
        if (!File.Exists(modelPath))
            throw new ArgumentException($"Whisper model not found: {modelPath}");

        var tempDir = Path.Combine(workspaceRoot, DefaultAudioOutputSubdir);
        Directory.CreateDirectory(tempDir);
        var normalizedWavPath = Path.Combine(tempDir, $"whisper-input-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");

        var ext = Path.GetExtension(audioPath).TrimStart('.').ToLowerInvariant();
        if (ext == "wav")
        {
            using var reader = new AudioFileReader(audioPath);
            ConvertToWhisperWav(reader, normalizedWavPath);
        }
        else if (!TryConvertToWavWithFfmpeg(audioPath, normalizedWavPath))
        {
            throw new ArgumentException(
                $"Unsupported audio format: .{ext}. Convert to WAV or install FFmpeg on PATH.");
        }

        var segments = new List<object>();
        var transcriptParts = new List<string>();
        try
        {
            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory
                .CreateBuilder()
                .WithLanguage(language)
                .Build();

            await using var fileStream = File.OpenRead(normalizedWavPath);
            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                var text = segment.Text?.Trim() ?? string.Empty;
                if (text.Length > 0)
                    transcriptParts.Add(text);

                if (segments.Count < maxSegments)
                {
                    segments.Add(new
                    {
                        start_sec = Math.Round(segment.Start.TotalSeconds, 3),
                        end_sec = Math.Round(segment.End.TotalSeconds, 3),
                        text
                    });
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(normalizedWavPath))
                    File.Delete(normalizedWavPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        var transcript = string.Join(" ", transcriptParts).Trim();
        return new
        {
            schema = Schema,
            ok = true,
            op = "transcribe",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · transcribe · {segments.Count}seg · {transcript.Length}c",
            success = true,
            audio_path = audioPath,
            model_path = modelPath,
            language,
            transcript,
            segments,
            segment_count = segments.Count,
            transcribed_at_utc = DateTime.UtcNow.ToString("O"),
            workspace = workspaceRoot,
            hint = "transcript + segments[] — analysis-mcp whisper parity"
        };
    }

    static void ConvertToWhisperWav(ISampleProvider reader, string normalizedWavPath)
    {
        if (reader.WaveFormat.Channels == 2)
        {
            reader = new StereoToMonoSampleProvider(reader)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }
        else if (reader.WaveFormat.Channels > 2)
        {
            throw new ArgumentException(
                $"Unsupported channel count: {reader.WaveFormat.Channels}. Use mono/stereo source.");
        }

        var resampled = new WdlResamplingSampleProvider(reader, 16000);
        WaveFileWriter.CreateWaveFile16(normalizedWavPath, resampled);
    }

    static bool TryConvertToWavWithFfmpeg(string inputPath, string outputWavPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                ArgumentList = { "-y", "-i", inputPath, "-acodec", "pcm_s16le", "-ar", "16000", "-ac", "1", outputWavPath },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;
            process.WaitForExit(TimeSpan.FromMinutes(5));
            return process.ExitCode == 0 && File.Exists(outputWavPath) && new FileInfo(outputWavPath).Length > 44;
        }
        catch
        {
            return false;
        }
    }

    static object AudioDeviceScene()
    {
        var waveIn = new List<object>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            waveIn.Add(new { index = i, name = caps.ProductName, channels = caps.Channels });
        }

        var wasapiActive = new List<string>();
        var wasapiDisabled = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                wasapiActive.Add(d.FriendlyName);
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Disabled))
                wasapiDisabled.Add(d.FriendlyName);
        }
        catch
        {
            // WASAPI probe is best-effort for scene.
        }

        return new
        {
            wavein_count = WaveInEvent.DeviceCount,
            wavein = waveIn,
            wasapi_active = wasapiActive,
            wasapi_disabled = wasapiDisabled
        };
    }

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

    /// <summary>OCR batch via external tesseract — analysis-mcp <c>ocr_image_batch</c> parity, in-proc desk.</summary>
    static object Ocr(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            throw new ArgumentException($"Workspace directory does not exist: {workspaceRoot}");

        var lang = (Opt(args, "lang") ?? "eng").Trim();
        var sampleEvery = Math.Clamp(GetOptionalInt(args, "sample_every", 1), 1, 100);
        var maxImages = Math.Clamp(GetOptionalInt(args, "max_images", 1000), 1, 10000);

        var singleFile = Opt(args, "file_path") ?? Opt(args, "image") ?? Opt(args, "path");
        string imagesDir;
        List<string> allImages;

        if (!string.IsNullOrWhiteSpace(singleFile))
        {
            var filePath = Path.IsPathRooted(singleFile)
                ? Path.GetFullPath(singleFile)
                : Path.GetFullPath(Path.Combine(workspaceRoot, singleFile));
            if (!filePath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("file_path points outside of workspace_path.");
            if (!File.Exists(filePath))
                throw new ArgumentException($"Image file does not exist: {filePath}");
            imagesDir = Path.GetDirectoryName(filePath) ?? workspaceRoot;
            allImages = [filePath];
        }
        else
        {
            var imagesDirInput = Opt(args, "images_dir") ?? Opt(args, "dir") ?? DefaultOutputSubdir;
            imagesDir = Path.IsPathRooted(imagesDirInput)
                ? Path.GetFullPath(imagesDirInput)
                : Path.GetFullPath(Path.Combine(workspaceRoot, imagesDirInput));
            if (!imagesDir.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("images_dir points outside of workspace_path.");
            if (!Directory.Exists(imagesDir))
                throw new ArgumentException($"Images directory does not exist: {imagesDir}");

            allImages = Directory
                .EnumerateFiles(imagesDir)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allImages.Count == 0)
                throw new ArgumentException("No image files found in images_dir.");
        }

        var sampledImages = allImages
            .Where((_, index) => index % sampleEvery == 0)
            .Take(maxImages)
            .ToList();
        if (sampledImages.Count == 0)
            sampledImages.Add(allImages[0]);

        var tesseractExe = ResolveTesseractExe();
        var pages = new List<object>();
        var errors = new List<object>();

        for (var i = 0; i < sampledImages.Count; i++)
        {
            var imagePath = sampledImages[i];
            var fileName = Path.GetFileName(imagePath);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tesseractExe,
                    Arguments = $"\"{imagePath}\" stdout -l {lang} --psm 3",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi)
                    ?? throw new ArgumentException($"Failed to start tesseract process for: {imagePath}");
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    errors.Add(new { file = imagePath, message = $"tesseract exited with code {process.ExitCode}: {error.Trim()}" });
                    continue;
                }

                pages.Add(new
                {
                    index = i + 1,
                    file = imagePath,
                    file_name = fileName,
                    text = output.Trim()
                });
            }
            catch (Exception ex)
            {
                errors.Add(new { file = imagePath, message = ex.Message });
            }
        }

        if (pages.Count == 0 && errors.Count > 0)
            throw new ArgumentException("OCR failed for all images. Check that tesseract is installed and accessible.");

        var outputJsonPathInput = Opt(args, "output_json_path");
        string outputJsonPath;
        if (!string.IsNullOrWhiteSpace(outputJsonPathInput))
        {
            outputJsonPath = Path.IsPathRooted(outputJsonPathInput)
                ? Path.GetFullPath(outputJsonPathInput)
                : Path.GetFullPath(Path.Combine(workspaceRoot, outputJsonPathInput));
        }
        else
        {
            outputJsonPath = Path.Combine(imagesDir, "ocr.json");
        }

        if (!outputJsonPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("output_json_path points outside of workspace_path.");

        var resultObject = new
        {
            success = true,
            workspace_path = workspaceRoot,
            images_dir = imagesDir,
            lang,
            sample_every = sampleEvery,
            max_images = maxImages,
            images_total = allImages.Count,
            images_processed = pages.Count,
            errors,
            pages,
            output_json_path = outputJsonPath,
            generated_at_utc = DateTime.UtcNow.ToString("O")
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath)!);
        File.WriteAllText(outputJsonPath, JsonSerializer.Serialize(resultObject, Pretty));

        var preview = pages
            .Select(p =>
            {
                var el = JsonSerializer.SerializeToElement(p);
                var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var name = el.TryGetProperty("file_name", out var n) ? n.GetString() ?? "" : "";
                var clip = text.Length > 240 ? text[..240] + "…" : text;
                return new { file_name = name, text_preview = clip };
            })
            .ToList();

        return new
        {
            schema = Schema,
            ok = true,
            op = "ocr",
            go = GoName,
            tool = ToolName,
            pulse = $"webcam · ocr · {pages.Count}/{allImages.Count} · {lang}",
            success = true,
            workspace = workspaceRoot,
            images_dir = imagesDir,
            lang,
            images_processed = pages.Count,
            images_total = allImages.Count,
            errors,
            pages,
            preview,
            output_json_path = outputJsonPath,
            generated_at_utc = resultObject.generated_at_utc,
            hint = "pages[].text — full OCR; output_json_path under images_dir."
        };
    }

    static string ResolveTesseractExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable(TesseractEnvKey);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        const string defaultPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
        return File.Exists(defaultPath) ? defaultPath : "tesseract";
    }

    static (int X, int Y, int Width, int Height) VirtualScreenBounds() =>
    (
        GetSystemMetrics(SmXVirtualScreen),
        GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen),
        GetSystemMetrics(SmCyVirtualScreen)
    );

    static void SaveBitmap(System.Drawing.Bitmap bitmap, string path, string imageFormat, int jpegQuality)
    {
        if (imageFormat == "png")
        {
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid)
            ?? throw new ArgumentException("JPEG encoder not available.");
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            (long)jpegQuality);
        bitmap.Save(path, encoder, ep);
    }

    const int SmXVirtualScreen = 76;
    const int SmYVirtualScreen = 77;
    const int SmCxVirtualScreen = 78;
    const int SmCyVirtualScreen = 79;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    static string ResolveWorkspace(SessionContext session, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath.Trim());
        if (!string.IsNullOrWhiteSpace(session.ProjectRoot))
            return Path.GetFullPath(session.ProjectRoot!);
        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
