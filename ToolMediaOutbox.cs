using System.Threading;
using ModelContextProtocol.Protocol;

namespace CdpMcp;

/// <summary>
/// Side-channel images for the current MCP tool call (AsyncLocal).
/// CallToolHandler drains into <see cref="ImageContentBlock"/> when the agent opted in
/// (<c>vision=true</c> / <c>see=true</c>) — never auto-inject without that flag.
/// </summary>
internal static class ToolMediaOutbox
{
    public const int MaxImages = 2;
    public const int MaxBytesPerImage = 2_500_000;

    static readonly AsyncLocal<List<(byte[] Bytes, string Mime)>?> Pending = new();

    public static void Clear()
    {
        Pending.Value = null;
    }

    public static bool TryAdd(byte[] bytes, string mimeType)
    {
        if (bytes.Length is 0 or > MaxBytesPerImage)
            return false;

        var mime = string.IsNullOrWhiteSpace(mimeType) ? "image/png" : mimeType.Trim();
        var list = Pending.Value ??= [];
        if (list.Count >= MaxImages)
            return false;

        list.Add((bytes, mime));
        return true;
    }

    public static IList<ContentBlock> BuildContent(string text)
    {
        var blocks = new List<ContentBlock>
        {
            new TextContentBlock { Text = ResponseCaps.CapText(text) }
        };

        var list = Pending.Value;
        Pending.Value = null;
        if (list is null || list.Count == 0)
            return blocks;

        // PNG last — text card first. Audience=assistant: for the model, not a user-only UI dump.
        foreach (var (bytes, mime) in list)
        {
            var img = ImageContentBlock.FromBytes(bytes, mime);
            img.Annotations = new Annotations
            {
                Audience = [Role.Assistant],
                Priority = 0.85f
            };
            blocks.Add(img);
        }

        return blocks;
    }
}
