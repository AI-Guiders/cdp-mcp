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

}
