#nullable enable
using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using MeAiChat = Microsoft.Extensions.AI.ChatMessage;
using MeAiRole = Microsoft.Extensions.AI.ChatRole;

namespace CdpMcp;

/// <summary>MEAI message/options + streaming bridge for Face Completions.</summary>
internal static partial class CitizenCompletions
{
    internal static IReadOnlyList<MeAiChat> BuildMeAiMessages(BuiltTurn built)
    {
        var list = new List<MeAiChat>(built.Messages.Count + 1)
        {
            new(MeAiRole.System, built.System)
        };

        var lastUserIdx = -1;
        for (var i = 0; i < built.Messages.Count; i++)
        {
            if (built.Messages[i].Role == "user")
                lastUserIdx = i;
        }

        for (var i = 0; i < built.Messages.Count; i++)
        {
            var m = built.Messages[i];
            var role = m.Role switch
            {
                "assistant" => MeAiRole.Assistant,
                "system" => MeAiRole.System,
                _ => MeAiRole.User
            };

            if (built.Vision is { } vision && i == lastUserIdx && m.Role == "user")
            {
                list.Add(new MeAiChat(role,
                [
                    new TextContent(m.Content),
                    new DataContent(vision.Bytes, vision.Mime)
                ]));
            }
            else
            {
                list.Add(new MeAiChat(role, m.Content));
            }
        }

        return list;
    }

    internal static ChatOptions BuildMeAiChatOptions(BuiltTurn built, int maxTokens)
    {
        var temperature = built.Mode == CitizenTurnMode.Dialog ? 0.6f : 0f;
        var opts = new ChatOptions
        {
            MaxOutputTokens = Math.Clamp(maxTokens, 64, 8192),
            Temperature = temperature
        };

        if (built.Mode != CitizenTurnMode.Dialog || built.Vision is not null)
        {
            opts.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["enable_thinking"] = false,
                ["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false }
            };
        }

        return opts;
    }

    static TurnResult TurnViaMeAi(
        BuiltTurn built,
        Resolved resolved,
        IChatClient client,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Stream = organ shape. Headers=TTFT until first chunk; Idle between updates; Overall hard cap.
        // Accumulate → ToChatResponse so usage/finish merge like MEAI intends (not junior GetResponseAsync).
        using var turnCts = CreateTurnCts(cancellationToken);
        try
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token);
            budgetCts.CancelAfter(HeadersTimeoutFor(built.Mode));
            void TouchIdle() => budgetCts.CancelAfter(IdleTimeout);

            var messages = BuildMeAiMessages(built);
            var options = BuildMeAiChatOptions(built, maxTokens);
            var updates = ConsumeMeAiStream(
                client.GetStreamingResponseAsync(messages, options, budgetCts.Token),
                budgetCts.Token,
                TouchIdle);

            var response = updates.ToChatResponse();
            // Cloud.ru/MEAI may emit UsageContent more than once; ToChatResponse can SUM them → absurd totals.
            // Honest usage = last UsageContent on the stream (SSE final chunk shape).
            var meta = MetaFromMeAi(response, LastUsageFromUpdates(updates));
            return FinishText(built, resolved, meta.Text, meta);
        }
        catch (OperationCanceledException oce)
        {
            return MapCancel(built, resolved, oce, cancellationToken, built.Mode);
        }
        catch (ClientResultException cre)
        {
            var code = cre.Status is > 0
                ? (System.Net.HttpStatusCode)cre.Status
                : System.Net.HttpStatusCode.BadGateway;
            return FailHttp(built, resolved, code, cre.Message);
        }
        catch (HttpRequestException ex)
        {
            return FailNetwork(built, resolved, ex);
        }
        catch (IOException ex)
        {
            return FailNetwork(built, resolved, ex);
        }
    }

    /// <summary>Tests + live: drain stream with Idle touch; first chunk ends TTFT budget.</summary>
    internal static List<ChatResponseUpdate> ConsumeMeAiStream(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        CancellationToken ct,
        Action touchIdle)
    {
        var updates = new List<ChatResponseUpdate>();
        var enumerator = stream.GetAsyncEnumerator(ct);
        try
        {
            var gotFirst = false;
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                if (!gotFirst)
                {
                    gotFirst = true;
                    touchIdle();
                }
                else
                    touchIdle();

                updates.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return updates;
    }

    internal static OpenAiExtract MetaFromMeAi(ChatResponse response, UsageDetails? streamUsage = null)
    {
        var text = (response.Text ?? "").Trim();
        var fromReasoning = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            var reasoning = ExtractMeAiReasoning(response);
            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                text = reasoning.Trim();
                fromReasoning = true;
            }
        }

        var usage = streamUsage ?? response.Usage;
        int? prompt = usage?.InputTokenCount is long pin ? (int)pin : null;
        int? completion = usage?.OutputTokenCount is long cout ? (int)cout : null;
        int? total = usage?.TotalTokenCount is long tot ? (int)tot : null;
        var finish = response.FinishReason?.ToString();
        return new OpenAiExtract(
            string.IsNullOrWhiteSpace(text) ? null : text,
            finish,
            fromReasoning,
            prompt,
            completion,
            total);
    }

    internal static UsageDetails? LastUsageFromUpdates(IReadOnlyList<ChatResponseUpdate> updates)
    {
        UsageDetails? last = null;
        foreach (var update in updates)
        {
            foreach (var part in update.Contents)
            {
                if (part is UsageContent usage)
                    last = usage.Details;
            }
        }

        return last;
    }

    static string? ExtractMeAiReasoning(ChatResponse response)
    {
        var sb = new StringBuilder();
        foreach (var message in response.Messages)
        {
            foreach (var part in message.Contents)
            {
                if (part is TextReasoningContent tr && !string.IsNullOrEmpty(tr.Text))
                    sb.Append(tr.Text);
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
