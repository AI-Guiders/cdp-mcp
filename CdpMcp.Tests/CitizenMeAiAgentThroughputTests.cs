#nullable enable
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace CdpMcp.Tests;

public sealed class CitizenMeAiAgentThroughputTests
{
    [Fact]
    public void BuildConcurrentFunctionClient_enables_AllowConcurrentInvocation()
    {
        var inner = new NoOpChatClient();
        var pipe = CitizenCompletions.BuildConcurrentFunctionClient(inner);
        var fic = pipe.GetService(typeof(FunctionInvokingChatClient)) as FunctionInvokingChatClient
            ?? pipe as FunctionInvokingChatClient;
        // ChatClientBuilder wraps — unwrap via GetService on the outer client.
        fic ??= FindFunctionInvoking(pipe);
        Assert.NotNull(fic);
        Assert.True(fic!.AllowConcurrentInvocation);
        Assert.True(fic.IncludeDetailedErrors);
    }

    [Fact]
    public void BuildFaceAgentOptions_passes_ChatOptions_multi_tool_and_keeps_custom_pipe()
    {
        var applied = new List<CitizenRouteHost.Applied>();
        Task<string> Exec(string name, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args, CancellationToken ct) =>
            Task.FromResult("ok");
        var tools = CitizenMeAiAgentTools.BuildWholeCatalog(Exec, applied);
        var built = new CitizenCompletions.BuiltTurn(
            System: "sys",
            Messages: [new CitizenCompletions.ChatMessage("user", "hi")],
            AfferentPulse: null,
            Injected: true,
            Mode: CitizenTurnMode.Wire,
            Vision: null);

        var opts = CitizenCompletions.BuildFaceAgentOptions(built, maxTokens: 2048, tools);
        Assert.True(opts.UseProvidedChatClientAsIs);
        Assert.NotNull(opts.ChatOptions);
        Assert.True(opts.ChatOptions!.AllowMultipleToolCalls);
        Assert.Equal(2048, opts.ChatOptions.MaxOutputTokens);
        Assert.NotNull(opts.ChatOptions.Tools);
        Assert.Equal(tools.Count, opts.ChatOptions.Tools!.Count);
        Assert.Contains("MULTIPLE tool calls", opts.ChatOptions.Instructions ?? "", StringComparison.Ordinal);
        Assert.Contains("Throughput", CitizenCompletions.BuildFaceAgentInstructions("sys"), StringComparison.Ordinal);
    }

    [Fact]
    public void AgentOverallTimeout_defaults_above_stream_Overall()
    {
        Assert.True(CitizenCompletions.AgentOverallTimeout > CitizenCompletions.OverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(180), CitizenCompletions.AgentOverallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(90), CitizenCompletions.OverallTimeout);
    }

    [Fact]
    public void ClipOutcome_clips_at_AgentToolClipChars()
    {
        Assert.Equal(4_000, CitizenMeAiAgentTools.AgentToolClipChars);
        var fat = new string('x', CitizenMeAiAgentTools.AgentToolClipChars + 50);
        var clipped = CitizenMeAiAgentTools.ClipOutcome(fat, CitizenMeAiAgentTools.AgentToolClipChars);
        Assert.StartsWith(new string('x', CitizenMeAiAgentTools.AgentToolClipChars), clipped);
        Assert.Contains("chars clipped", clipped, StringComparison.Ordinal);
        Assert.True(clipped.Length < fat.Length);
    }

    static FunctionInvokingChatClient? FindFunctionInvoking(IChatClient client)
    {
        for (var cur = client; cur is not null;)
        {
            if (cur is FunctionInvokingChatClient fic)
                return fic;
            var next = cur.GetService(typeof(IChatClient)) as IChatClient;
            if (ReferenceEquals(next, cur))
                break;
            cur = next!;
        }

        return null;
    }

    sealed class NoOpChatClient : IChatClient
    {
        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Empty();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        static async IAsyncEnumerable<ChatResponseUpdate> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
