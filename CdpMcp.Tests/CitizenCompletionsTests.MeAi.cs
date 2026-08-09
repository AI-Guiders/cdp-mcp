#nullable enable
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Xunit;

namespace CdpMcp.Tests;

public partial class CitizenCompletionsTests
{
    [Fact]
    public void Turn_meai_stream_merges_usage_via_ToChatResponse()
    {
        CitizenCostLedger.ResetForTests();
        CitizenCostLedger.SetTestMemory(null, null, memoryOnly: true);
        CitizenCompletions.TestOpenAiApiKey = "sk-cloud-ru-test-abcdefghijklmnop";
        CitizenCompletions.TestOpenAiBaseUrl = "https://foundation-models.api.cloud.ru/v1";
        CitizenCompletions.TestChatClient = new FakeMeAiStreamClient();
        try
        {
            var r = CitizenCompletions.Turn("ping", dryRun: false, inject: false, mode: CitizenTurnMode.Dialog);
            Assert.True(r.Ok, r.Error + " · " + r.Hint);
            Assert.Equal("alive-stream", r.Text);
            Assert.Contains("prompt_tokens=11", r.Hint!, StringComparison.Ordinal);
            Assert.Contains("completion_tokens=7", r.Hint!, StringComparison.Ordinal);
        }
        finally
        {
            CitizenCompletions.TestChatClient = null;
            CitizenCompletions.TestOpenAiApiKey = null;
            CitizenCompletions.TestOpenAiBaseUrl = null;
            CitizenCostLedger.ResetForTests();
        }
    }

    [Fact]
    public void MetaFromMeAi_prefers_content_over_reasoning()
    {
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new TextReasoningContent("hidden"),
                new TextContent("visible"),
            ])
        ])
        {
            Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2, TotalTokenCount = 5 }
        };

        var meta = CitizenCompletions.MetaFromMeAi(response);
        Assert.Equal("visible", meta.Text);
        Assert.False(meta.FromReasoning);
        Assert.Equal(3, meta.PromptTokens);
        Assert.Equal(2, meta.CompletionTokens);
    }

    sealed class FakeMeAiStreamClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("stream-only fake");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            // Role only on first update (SSE shape). Contents= must not wipe TextContent — that was the fake bug.
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("alive-")]);
            yield return new ChatResponseUpdate { Contents = [new TextContent("stream")] };
            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop,
                Contents =
                [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 11,
                        OutputTokenCount = 7,
                        TotalTokenCount = 18
                    })
                ]
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
