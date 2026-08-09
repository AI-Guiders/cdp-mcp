#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CdpMcp.Tests;
public partial class CitizenCompletionsTests : IDisposable
{
    [Fact]
    public void Turn_openai_compat_uses_reasoning_when_content_empty()
    {
        var payload = """
            {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"","reasoning_content":"@intent go=plan\nok"}}],"usage":{"completion_tokens":400}}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("status?", dryRun: false);
            Assert.True(r.Ok);
            Assert.Contains("@intent go=plan", r.Text!);
            Assert.Contains("text from reasoning", r.Hint!, StringComparison.Ordinal);
            Assert.Equal("plan", r.Routes![0].Go);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_records_cost_ledger_and_prompt_tokens_hint()
    {
        CitizenCostLedger.ResetForTests();
        CitizenCostLedger.SetTestMemory(null, null, memoryOnly: true);
        var payload = """
            {"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"ok"}}],"usage":{"prompt_tokens":77,"completion_tokens":5,"total_tokens":82}}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("ping", dryRun: false, inject: false, mode: CitizenTurnMode.Wire);
            Assert.True(r.Ok);
            Assert.Contains("prompt_tokens=77", r.Hint!, StringComparison.Ordinal);
            Assert.Contains("completion_tokens=5", r.Hint!, StringComparison.Ordinal);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(CitizenCostLedger.Pulse()));
            Assert.Equal(1, doc.RootElement.GetProperty("turns").GetInt32());
            Assert.Equal(77, doc.RootElement.GetProperty("prompt_tokens").GetInt64());
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
            CitizenCostLedger.ResetForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_empty_text_surfaces_finish_reason_length()
    {
        var payload = """
            {"choices":[{"finish_reason":"length","message":{"role":"assistant","content":"","reasoning_content":""}}],"usage":{"completion_tokens":1800,"prompt_tokens":500}}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("long?", dryRun: false, mode: CitizenTurnMode.Dialog);
            Assert.False(r.Ok);
            Assert.Equal("empty_text", r.Error);
            Assert.Contains("finish_reason=length", r.Hint!, StringComparison.Ordinal);
            Assert.Contains("completion_tokens=1800", r.Hint!, StringComparison.Ordinal);
            Assert.Contains("truncated", r.Hint!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_streams_reasoning_deltas_when_content_absent()
    {
        var sse = """
            data: {"choices":[{"delta":{"reasoning_content":"@intent "}}]}

            data: {"choices":[{"delta":{"reasoning_content":"go=plan\n"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, sse, "text/event-stream");
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("status?", dryRun: false);
            Assert.True(r.Ok);
            Assert.Contains("@intent go=plan", r.Text!);
            Assert.Contains("text from reasoning", r.Hint!, StringComparison.Ordinal);
            Assert.Equal("plan", r.Routes![0].Go);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_parses_choices_message()
    {
        var payload = """
            {"choices":[{"message":{"role":"assistant","content":"@intent go=plan\nok"}}]}
            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, payload);
        CitizenCompletions.ResetHttpForTests();
        var r = CitizenCompletions.Turn("status?", dryRun: false);
        Assert.True(r.Ok);
        Assert.Equal(CitizenCompletions.ProviderOpenAiCompat, r.Provider);
        Assert.Equal(CitizenAiKeys.DefaultOpenAiModel, r.Model);
        Assert.Contains("@intent go=plan", r.Text!);
        Assert.NotNull(r.Routes);
        Assert.Single(r.Routes!);
        Assert.Equal("plan", r.Routes[0].Go);
    }

    [Fact]
    public void Turn_openai_compat_streams_sse_deltas()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"@intent "}}]}

            data: {"choices":[{"delta":{"content":"go=plan\n"}}]}

            data: [DONE]

            """;
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHandler = new StubHandler(HttpStatusCode.OK, sse, "text/event-stream");
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("status?", dryRun: false);
            Assert.True(r.Ok);
            Assert.Equal(CitizenCompletions.ProviderOpenAiCompat, r.Provider);
            Assert.Contains("@intent go=plan", r.Text!);
            Assert.NotNull(r.Routes);
            Assert.Single(r.Routes!);
            Assert.Equal("plan", r.Routes[0].Go);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_headers_timeout_returns_timeout_error()
    {
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHeadersTimeout = TimeSpan.FromMilliseconds(80);
        CitizenCompletions.TestOverallTimeout = TimeSpan.FromSeconds(5);
        CitizenCompletions.TestMaxAttempts = 1; // no reconnect — surface timeout once
        CitizenCompletions.TestHandler = new HangHeadersHandler(TimeSpan.FromSeconds(3));
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("status?", dryRun: false);
            Assert.False(r.Ok);
            Assert.Equal("timeout", r.Error);
            Assert.Contains("http_budget", r.Hint!, StringComparison.Ordinal);
        }
        finally
        {
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHeadersTimeout = null;
            CitizenCompletions.TestOverallTimeout = null;
            CitizenCompletions.TestMaxAttempts = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void Turn_openai_compat_reconnects_after_headers_timeout()
    {
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestHeadersTimeout = TimeSpan.FromMilliseconds(80);
        CitizenCompletions.TestOverallTimeout = TimeSpan.FromSeconds(5);
        CitizenCompletions.TestMaxAttempts = 3;
        var okPayload = """
            {"choices":[{"message":{"content":"жив после reconnect"}}],"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}
            """;
        var hookHits = 0;
        CitizenCompletions.TransientRetryHook = (_, _, _) => hookHits++;
        CitizenCompletions.TestHandler = new SequenceHandler(
            new HangHeadersHandler(TimeSpan.FromSeconds(3)),
            new StubHandler(HttpStatusCode.OK, okPayload));
        CitizenCompletions.ResetHttpForTests();
        try
        {
            var r = CitizenCompletions.Turn("ping", dryRun: false);
            Assert.True(r.Ok);
            Assert.Equal("жив после reconnect", r.Text);
            Assert.True(hookHits >= 1);
            Assert.Contains("reconnect ok", r.Hint!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CitizenCompletions.TransientRetryHook = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCompletions.TestHeadersTimeout = null;
            CitizenCompletions.TestOverallTimeout = null;
            CitizenCompletions.TestMaxAttempts = null;
            CitizenCompletions.TestHandler = null;
            CitizenCompletions.ResetHttpForTests();
        }
    }

    [Fact]
    public void IsTransientError_covers_timeout_and_gateway()
    {
        Assert.True(CitizenCompletions.IsTransientError("timeout"));
        Assert.True(CitizenCompletions.IsTransientError("http_503"));
        Assert.True(CitizenCompletions.IsTransientError("http_network"));
        Assert.False(CitizenCompletions.IsTransientError("empty_text"));
        Assert.False(CitizenCompletions.IsTransientError("http_401"));
    }

    [Fact]
    public void HeadersTimeoutFor_dialog_longer_than_wire()
    {
        Assert.True(CitizenCompletions.HeadersTimeoutFor(CitizenTurnMode.Dialog)
            > CitizenCompletions.HeadersTimeoutFor(CitizenTurnMode.Wire));
    }

    [Fact]
    public void ChatCompletionsUrl_normalizes_cloud_ru_base()
    {
        Assert.Equal("https://foundation-models.api.cloud.ru/v1/chat/completions", CitizenCompletions.ChatCompletionsUrl("https://foundation-models.api.cloud.ru"));
        Assert.Equal("https://foundation-models.api.cloud.ru/v1/chat/completions", CitizenCompletions.ChatCompletionsUrl("https://foundation-models.api.cloud.ru/v1/"));
    }

    [Fact]
    public void Channel_turn_dry_run_json_ok()
    {
        using var doc = JsonDocument.Parse("""
            {"op":"turn","message":"hello","dry_run":true,"board":"P  plan · test"}
            """);
        var args = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        var json = IdeCitizenChannel.HandleJson(args);
        using var outDoc = JsonDocument.Parse(json);
        Assert.True(outDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("dry_run").GetBoolean());
        Assert.True(outDoc.RootElement.GetProperty("injected").GetBoolean());
    }

    [Fact]
    public void Channel_scene_exposes_invite_ready_gate()
    {
        var json = IdeCitizenChannel.HandleJson(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase) { ["op"] = JsonSerializer.SerializeToElement("scene") });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("invite_ready", out var invite));
        Assert.True(invite.TryGetProperty("Status", out _) || invite.TryGetProperty("status", out _) || invite.ValueKind == JsonValueKind.Object);
        Assert.Contains("invite=", doc.RootElement.GetProperty("pulse").GetString(), StringComparison.Ordinal);
    }
}