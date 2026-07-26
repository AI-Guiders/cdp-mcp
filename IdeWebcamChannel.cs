#nullable enable
using System.Text.Json;
using Cdp.Core;
using OpenCvSharp;
using WebcamMcp.Shared;
using static WebcamMcp.Shared.McpDefaults;
using static WebcamMcp.Shared.ToolArgs;

namespace CdpMcp;

/// <summary>
/// Soft organ <c>go=webcam_desk</c> / Meta <c>cdp_webcam</c> — in-proc sense plane via
/// <c>AIGuiders.WebcamMcp.Shared</c> + OpenCv (not parked Cursor webcam-mcp guest).
/// Capture ops aligned with webcam-capture-mcp; analysis later.
/// </summary>
internal static class IdeWebcamChannel
{
    public const string Schema = "webcam/v0";
    public const string ToolName = "cdp_webcam";
    public const string GoName = "webcam_desk";

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

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
            pulse = "webcam · in-proc Shared+OpenCv · frame",
            workspace = root,
            core = "AIGuiders.WebcamMcp.Shared 0.1.0",
            ops = new[] { "scene", "frame" },
            planned = new[] { "burst", "screen", "audio", "av", "analyze", "transcribe" },
            hint = "op=frame [camera_index=0][file_name=hello] — saves under .cascade-ide/webcam-captures"
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
