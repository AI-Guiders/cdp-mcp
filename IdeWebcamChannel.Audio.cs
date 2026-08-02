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

}
