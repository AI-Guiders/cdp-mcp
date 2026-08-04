#nullable enable
namespace CdpMcp;

/// <summary>
/// One-shot vision frame for the next citizen turn (OpenAI-compat multimodal).
/// Armed by <c>cdp_see</c> or <c>cdp_citizen image_path=</c>; consumed on Turn.
/// Not stored in dialog history (base64 stays out of jsonl).
/// </summary>
internal static class CitizenVisionLatch
{
    public const int MaxBytes = ToolMediaOutbox.MaxBytesPerImage;
    public const string DefaultVisionModel = "Qwen/Qwen3.6-35B-A3B";

    public sealed record Frame(byte[] Bytes, string Mime, string? Path);

    static readonly object Gate = new();
    static Frame? Pending;

    public static void Arm(byte[] bytes, string mime, string? path = null)
    {
        if (bytes.Length is 0 or > MaxBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes), "vision frame empty or > MaxBytes");
        var m = string.IsNullOrWhiteSpace(mime) ? "image/png" : mime.Trim();
        lock (Gate)
            Pending = new Frame(bytes, m, path);
    }

    public static Frame? Peek()
    {
        lock (Gate) return Pending;
    }

    public static Frame? Take()
    {
        lock (Gate)
        {
            var f = Pending;
            Pending = null;
            return f;
        }
    }

    public static void Clear()
    {
        lock (Gate) Pending = null;
    }

    public static void ResetForTests() => Clear();

    public static Frame LoadPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full))
            throw new FileNotFoundException("vision image not found", full);
        var bytes = File.ReadAllBytes(full);
        if (bytes.Length is 0 or > MaxBytes)
            throw new InvalidOperationException($"vision image size {bytes.Length} not in 1..{MaxBytes}");
        var mime = GuessMime(full);
        return new Frame(bytes, mime, full);
    }

    public static bool ModelLooksNonVision(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return true;
        var m = model.Trim();
        if (m.Equals(CitizenAiKeys.DefaultOpenAiModel, StringComparison.OrdinalIgnoreCase))
            return true;
        if (m.Contains("Coder", StringComparison.OrdinalIgnoreCase)
            && !m.Contains("-VL", StringComparison.OrdinalIgnoreCase)
            && !m.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            return true;
        // Known internal text/reasoning without Vision flag
        if (m.Contains("GLM-5.1", StringComparison.OrdinalIgnoreCase)
            || m.Contains("GLM-4.7", StringComparison.OrdinalIgnoreCase)
            || m.Contains("GigaChat3", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static string GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
}
