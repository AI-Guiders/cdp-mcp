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

/// <summary>
/// Soft organ <c>go=webcam_desk</c> / Meta <c>cdp_webcam</c> — in-proc sense plane via
/// <c>AIGuiders.WebcamMcp.Shared</c> + OpenCv (not parked Cursor webcam-mcp guest).
/// Capture + OCR aligned with webcam-*-mcp split; more analysis later.
/// </summary>
internal static partial class IdeWebcamChannel
{
    public const string Schema = "webcam/v0";
    public const string ToolName = "cdp_webcam";
    public const string GoName = "webcam_desk";

    /// <summary>Env override for tesseract.exe (analysis-mcp parity).</summary>
    const string TesseractEnvKey = "WEBCAM_MCP_TESSERACT_PATH";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static readonly object GlassGate = new();
    static GlassSnap? _lastGlass;

    sealed record GlassSnap(string Op, string Pulse, string? Path, DateTimeOffset StampedUtc);

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
            var result = op switch
            {
                "scene" or "status" or "caps" => Scene(session),
                "frame" or "snap" or "capture" or "photo" => Frame(session, args),
                "burst" or "webcam_burst" or "capture_burst" => Burst(session, args),
                "av" or "av_burst" or "capture_av" or "capture_av_burst" => Av(session, args),
                "screen" or "screen_burst" or "capture_screen_burst" => Screen(session, args),
                "window" or "window_snap" or "capture_window" or "window_list" or "windows"
                    or "list_windows" => Window(session, args),
                "audio" or "record_audio" or "capture_audio" => Audio(session, args),
                "transcribe" or "transcribe_audio" or "transcribe_audio_whisper" or "whisper" =>
                    Transcribe(session, args),
                "ocr" or "ocr_batch" or "ocr_image_batch" => Ocr(session, args),
                "analyze" or "analyze_burst" or "analyze_burst_sequence" => Analyze(session, args),
                _ => Scene(session)
            };
            RememberGlass(op, result);
            return result;
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

    /// <summary>Mirror last capture pulse to flat CIDE chrome latch (not EICAS).</summary>
    public static void PublishGlass()
    {
        try
        {
            GlassSnap? snap;
            lock (GlassGate)
                snap = _lastGlass;

            if (snap is null)
            {
                CideWebcamLatch.Publish(active: false, pulse: "webcam · idle", op: null, path: null);
                return;
            }

            // Dark Cockpit: chrome only while capture evidence exists.
            CideWebcamLatch.Publish(
                active: true,
                pulse: snap.Pulse,
                op: snap.Op,
                path: snap.Path);
        }
        catch
        {
            /* best-effort */
        }
    }

    static void RememberGlass(string op, object result)
    {
        try
        {
            if (op is "scene" or "status" or "caps")
                return;

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl)
                && okEl.ValueKind == JsonValueKind.False)
                return;
            if (!root.TryGetProperty("pulse", out var pulseEl))
                return;
            var pulse = pulseEl.GetString();
            if (string.IsNullOrWhiteSpace(pulse))
                return;

            string? path = null;
            if (root.TryGetProperty("file_path", out var filePath))
                path = filePath.GetString();
            else if (root.TryGetProperty("audio_path", out var audioPath))
                path = audioPath.GetString();

            var wireOp = root.TryGetProperty("op", out var opEl) ? opEl.GetString() : op;
            wireOp = string.IsNullOrWhiteSpace(wireOp) ? op : wireOp.Trim();

            lock (GlassGate)
                _lastGlass = new GlassSnap(wireOp!, pulse.Trim(), path, DateTimeOffset.UtcNow);

            PublishGlass();
        }
        catch
        {
            /* best-effort */
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
            pulse = "webcam · in-proc · frame|burst|av|screen|window|audio|transcribe|ocr|analyze",
            workspace = root,
            core = "AIGuiders.WebcamMcp.Shared 0.1.0",
            ops = new[]
            {
                "scene", "frame", "burst", "av", "screen", "window", "window_list",
                "audio", "transcribe", "ocr", "analyze"
            },
            planned = Array.Empty<string>(),
            audio = AudioDeviceScene(),
            whisper_model = Environment.GetEnvironmentVariable(EnvWhisperModelPath),
            hint = "op=window process=|title=|hwnd= — HWND PNG (PrintWindow); op=window_list to discover"
        };
    }

}
