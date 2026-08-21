using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace CdpMcpBridge;

/// <summary>Extract per-chat identity from MCP CallTool _meta (ADR-0200 multi-chat).</summary>
internal static class CdpBridgeConversationMeta
{
    static readonly string[] ComposerIdKeys =
    [
        "cursor/composerId",
        "cursor/composer_id",
        "composerId",
        "composer_id",
        "conversationId",
        "conversation_id"
    ];

    public static string? TryResolve(CallToolRequestParams? parameters) =>
        TryResolve(parameters?.Meta);

    public static string? TryResolve(JsonObject? meta)
    {
        if (meta is null)
            return null;

        foreach (var key in ComposerIdKeys)
        {
            if (meta[key] is not JsonValue value)
                continue;
            var text = value.GetValue<string?>()?.Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        // Cursor today: progressToken only — scopes one agent turn (better than bridge-global).
        if (meta["progressToken"] is JsonValue progress)
        {
            if (progress.TryGetValue(out string? progressText) && !string.IsNullOrWhiteSpace(progressText))
                return "pt:" + progressText.Trim();
            if (progress.TryGetValue(out long progressNum))
                return "pt:" + progressNum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    }
}
