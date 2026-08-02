#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;
internal static partial class CitizenCompletions
{
    static TurnResult TurnAnthropic(BuiltTurn built, Resolved resolved, int maxTokens, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = resolved.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 64, 8192),
            ["system"] = built.System,
            ["messages"] = built.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        req.Headers.TryAddWithoutValidation("x-api-key", resolved.ApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = Http.SendAsync(req, cancellationToken).GetAwaiter().GetResult();
        var body = resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
        {
            return FailHttp(built, resolved, resp.StatusCode, body);
        }

        var text = ExtractAnthropicText(body);
        return FinishText(built, resolved, text);
    }

    static string? ExtractAnthropicText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "text" && block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(textEl.GetString());
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}