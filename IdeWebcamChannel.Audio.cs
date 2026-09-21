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
    /// <summary>Mic WAV via NAudio 3 WasapiRecorder — capture-mcp <c>capture_audio</c> parity.</summary>
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

        using var capture = IdeWebcamWasapiCapture.OpenSession(outputPath, deviceNumber, sampleRate, channels);
        capture.StartRecording();
        Thread.Sleep(durationSec * 1000);
        capture.StopRecording();
        capture.WaitForStop(TimeSpan.FromSeconds(5));

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
            pulse = $"webcam · audio · wasapi · {durationSec}s · {sampleRate}Hz · {channels}ch · {fileInfo.Length}B",
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


    static object AudioDeviceScene()
    {
        List<object> capture = [];
        List<string> wasapiActive = [];
        List<string> wasapiDisabled = [];
        try
        {
            foreach (var d in IdeWebcamWasapiCapture.ListActiveCaptureDevices())
                capture.Add(new { index = d.Index, name = d.Name });
            wasapiActive = IdeWebcamWasapiCapture.ListCaptureDeviceNames(DeviceState.Active).ToList();
            wasapiDisabled = IdeWebcamWasapiCapture.ListCaptureDeviceNames(DeviceState.Disabled).ToList();
        }
        catch
        {
            // WASAPI probe is best-effort for scene.
        }

        return new
        {
            capture_count = capture.Count,
            capture_devices = capture,
            wasapi_active = wasapiActive,
            wasapi_disabled = wasapiDisabled
        };
    }

}
