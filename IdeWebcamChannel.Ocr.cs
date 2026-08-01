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
