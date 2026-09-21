#nullable enable
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CdpMcp;

/// <summary>WASAPI mic capture (NAudio 3) — cross-platform build, Windows runtime only.</summary>
[SupportedOSPlatform("windows")]
internal static class IdeWebcamWasapiCapture
{
    internal sealed record DeviceInfo(int Index, string Name);

    internal static IReadOnlyList<DeviceInfo> ListActiveCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<DeviceInfo>();
        var index = 0;
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            list.Add(new DeviceInfo(index++, device.FriendlyName));
        return list;
    }

    internal static IReadOnlyList<string> ListCaptureDeviceNames(DeviceState state)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, state)
            .Select(d => d.FriendlyName)
            .ToList();
    }

    internal static WasapiWavSession OpenSession(
        string outputPath,
        int deviceIndex,
        int sampleRate,
        int channels,
        object? writeLock = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        if (endpoints.Count == 0)
            throw new ArgumentException("No WASAPI capture devices were found.");
        if (deviceIndex < 0 || deviceIndex >= endpoints.Count)
            throw new ArgumentException(
                $"device_number {deviceIndex} is out of range. Available devices: {endpoints.Count}.");

        return new WasapiWavSession(endpoints[deviceIndex], outputPath, sampleRate, channels, writeLock);
    }

    internal sealed class WasapiWavSession : IDisposable
    {
        readonly WasapiRecorder _recorder;
        readonly WaveFileWriter _writer;
        readonly ManualResetEventSlim _completed = new(false);
        readonly object? _writeLock;

        public Exception? RecordingError { get; private set; }

        public WaveFormat WaveFormat => _recorder.WaveFormat;

        internal WasapiWavSession(
            MMDevice device,
            string outputPath,
            int sampleRate,
            int channels,
            object? writeLock)
        {
            _writeLock = writeLock;
            _recorder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithFormat(new WaveFormat(sampleRate, 16, channels))
                .WithBufferLength(50)
                .Build();
            _writer = new WaveFileWriter(outputPath, _recorder.WaveFormat);
            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += (_, args) =>
            {
                RecordingError = args.Exception;
                _completed.Set();
            };
        }

        void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            if (flags.HasFlag(AudioClientBufferFlags.Silent))
                return;

            if (_writeLock is null)
            {
                _writer.Write(buffer);
                _writer.Flush();
                return;
            }

            lock (_writeLock)
            {
                _writer.Write(buffer);
                _writer.Flush();
            }
        }

        public void StartRecording() => _recorder.StartRecording();

        public void StopRecording() => _recorder.StopRecording();

        public void WaitForStop(TimeSpan timeout)
        {
            if (!_completed.Wait(timeout))
                throw new ArgumentException("Timeout while finalizing audio recording.");
            if (RecordingError is not null)
                throw new ArgumentException("Audio capture failed: " + RecordingError.Message);
        }

        public void Dispose()
        {
            try { _recorder.StopRecording(); } catch { /* already stopped */ }
            _recorder.Dispose();
            _writer.Dispose();
            _completed.Dispose();
        }
    }
}
