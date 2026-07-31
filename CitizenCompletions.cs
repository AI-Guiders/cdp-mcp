#nullable enable
using System.Text;
using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// In-habitat completions host (ADR-0028 / peel #9).
/// Anthropic Messages API via <see cref="CitizenAiKeys"/>; wire inject via <see cref="CitizenWire"/>.
/// </summary>
internal static class CitizenCompletions
{
    public const string DefaultModel = "claude-sonnet-4-20250514";
    public const string AnthropicVersion = "2023-06-01";
    public const string MessagesUrl = "https://api.anthropic.com/v1/messages";

    static readonly object HttpGate = new();
    static HttpClient? SharedHttp;
    static HttpMessageHandler? BoundHandler;

    /// <summary>Tests: inject handler before Turn; clear in finally.</summary>
    internal static HttpMessageHandler? TestHandler;

    /// <summary>Tests: force Anthropic key without touching disk.</summary>
    internal static string? TestApiKey;


    public sealed record ChatMessage(string Role, string Content);

    public sealed record BuiltTurn(
        string System,
        IReadOnlyList<ChatMessage> Messages,
        string? AfferentPulse,
        bool Injected);

    public sealed record TurnResult(
        bool Ok,
        string? Error,
        string? Hint,
        string? Text,
        string? Model,
        string? Provider,
        BuiltTurn? Built,
        IReadOnlyList<CitizenWireParser.Message>? WireIntents,
        bool DryRun);

    internal static void ResetHttpForTests()
    {
        lock (HttpGate)
        {
            SharedHttp?.Dispose();
            SharedHttp = null;
            BoundHandler = null;
        }
    }

    static HttpClient Http
    {
        get
        {
            lock (HttpGate)
            {
                if (SharedHttp is null || !ReferenceEquals(BoundHandler, TestHandler))
                {
                    SharedHttp?.Dispose();
                    HttpMessageHandler handler = TestHandler ?? new HttpClientHandler();
                    BoundHandler = TestHandler;
                    SharedHttp = new HttpClient(handler, disposeHandler: TestHandler is null)
                    {
                        Timeout = TimeSpan.FromSeconds(120)
                    };
                }

                return SharedHttp;
            }
        }
    }

    public static BuiltTurn Build(
        string userText,
        IEnumerable<string>? boardLines = null,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        bool inject = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);
        string? afferent = null;
        var injected = false;
        var msgs = new List<ChatMessage>();

        if (inject)
        {
            var prev = CitizenWire.Inject;
            try
            {
                CitizenWire.Inject = true;
                afferent = CitizenWire.PackFromDeskBoard(
                    boardLines,
                    sa: sa ?? "clear · explore/code",
                    peer: peer ?? "ok · gen=1 · mcp=live · compact=no",
                    next: next,
                    tm: tm);
                var bodies = CitizenWire.PrependAfferent([userText.Trim()], afferent);
                foreach (var b in bodies)
                    msgs.Add(new ChatMessage("user", b));
                injected = bodies.Count > 1;
            }
            finally
            {
                CitizenWire.Inject = prev;
            }
        }
        else
        {
            msgs.Add(new ChatMessage("user", userText.Trim()));
        }

        return new BuiltTurn(CitizenPersona.SystemPrompt, msgs, afferent, injected);
    }

    public static TurnResult Turn(
        string userText,
        IEnumerable<string>? boardLines = null,
        string? sa = null,
        string? peer = null,
        string? next = null,
        string? tm = null,
        string? model = null,
        bool dryRun = false,
        bool inject = true,
        int maxTokens = 1024,
        CancellationToken cancellationToken = default)
    {
        var built = Build(userText, boardLines, sa, peer, next, tm, inject);
        if (dryRun)
        {
            return new TurnResult(
                Ok: true,
                Error: null,
                Hint: "dry_run — no provider call; messages built with persona + wire inject",
                Text: null,
                Model: model ?? DefaultModel,
                Provider: "dry_run",
                Built: built,
                WireIntents: null,
                DryRun: true);
        }

        var keys = CitizenAiKeys.Load();
        var apiKey = TestApiKey ?? keys.AnthropicApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new TurnResult(
                false,
                "keys_missing",
                "set anthropic_api_key in %LocalAppData%\\CascadeIDE\\ai-keys.toml (CDP-ADR-0026)",
                null,
                model ?? DefaultModel,
                "anthropic",
                built,
                null,
                false);
        }

        var useModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = useModel,
                ["max_tokens"] = Math.Clamp(maxTokens, 64, 8192),
                ["system"] = built.System,
                ["messages"] = built.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
            };
            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = Http.SendAsync(req, cancellationToken).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return new TurnResult(
                    false,
                    "http_" + (int)resp.StatusCode,
                    Trunc(body, 240),
                    null,
                    useModel,
                    "anthropic",
                    built,
                    null,
                    false);
            }

            var text = ExtractAnthropicText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new TurnResult(
                    false,
                    "empty_text",
                    "provider returned no text blocks",
                    null,
                    useModel,
                    "anthropic",
                    built,
                    null,
                    false);
            }

            var intents = CitizenWireParser.Parse(text);
            return new TurnResult(
                true,
                null,
                intents.Count > 0
                    ? "ok — wire intents parsed from reply"
                    : "ok — reply has no @frame/@intent/@event lines",
                text,
                useModel,
                "anthropic",
                built,
                intents,
                false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new TurnResult(
                false,
                "turn_failed",
                Trunc(ex.Message, 240),
                null,
                useModel,
                "anthropic",
                built,
                null,
                false);
        }
    }

    static string? ExtractAnthropicText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t)
                && t.ValueKind == JsonValueKind.String
                && t.GetString() == "text"
                && block.TryGetProperty("text", out var textEl)
                && textEl.ValueKind == JsonValueKind.String)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(textEl.GetString());
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    static string? Trunc(string? s, int max)
    {
        if (s is null) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
