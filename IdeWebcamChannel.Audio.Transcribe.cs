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
    /// <summary>Whisper transcription — analysis-mcp <c>transcribe_audio_whisper</c> parity.</summary>
    static object Transcribe(SessionContext session, IReadOnlyDictionary<string, JsonElement> args) => TranscribeAsync(session, args).GetAwaiter().GetResult();
    static async Task<object> TranscribeAsync(SessionContext session, IReadOnlyDictionary<string, JsonElement> args)
    {
        var workspace = ResolveWorkspace(session, Opt(args, "workspace_path") ?? Opt(args, "workspace"));
        var workspaceRoot = Path.GetFullPath(workspace.Trim());
        if (File.Exists(workspaceRoot))
            workspaceRoot = Path.GetDirectoryName(workspaceRoot) ?? workspaceRoot;
        if (!Directory.Exists(workspaceRoot))
            throw new ArgumentException($"Workspace directory does not exist: {workspaceRoot}");
        var audioPathInput = Opt(args, "audio_path") ?? Opt(args, "file_path") ?? Opt(args, "path") ?? throw new ArgumentException("audio_path / file_path is required.");
        var language = (Opt(args, "language") ?? Opt(args, "lang") ?? "auto").Trim().ToLowerInvariant();
        // OCR also uses lang= — if caller passed eng/rus tess style, treat as auto for whisper.
        if (language is "eng" or "rus" or "eng+rus" or "rus+eng")
            language = "auto";
        var maxSegments = Math.Clamp(GetOptionalInt(args, "max_segments", 1000), 1, 5000);
        var audioPath = Path.IsPathRooted(audioPathInput) ? Path.GetFullPath(audioPathInput) : Path.GetFullPath(Path.Combine(workspaceRoot, audioPathInput));
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
            throw new ArgumentException($"Unsupported audio format: .{ext}. Convert to WAV or install FFmpeg on PATH.");
        }

        var segments = new List<object>();
        var transcriptParts = new List<string>();
        try
        {
            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder().WithLanguage(language).Build();
            await using var fileStream = File.OpenRead(normalizedWavPath);
            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                var text = segment.Text?.Trim() ?? string.Empty;
                if (text.Length > 0)
                    transcriptParts.Add(text);
                if (segments.Count < maxSegments)
                {
                    segments.Add(new { start_sec = Math.Round(segment.Start.TotalSeconds, 3), end_sec = Math.Round(segment.End.TotalSeconds, 3), text });
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
            throw new ArgumentException($"Unsupported channel count: {reader.WaveFormat.Channels}. Use mono/stereo source.");
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
                ArgumentList =
                {
                    "-y",
                    "-i",
                    inputPath,
                    "-acodec",
                    "pcm_s16le",
                    "-ar",
                    "16000",
                    "-ac",
                    "1",
                    outputWavPath
                },
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
}