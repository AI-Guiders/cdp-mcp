#nullable enable
using System.ClientModel;
using System.Text;
using Microsoft.Extensions.AI;
using MeAiChat = Microsoft.Extensions.AI.ChatMessage;
using MeAiRole = Microsoft.Extensions.AI.ChatRole;

namespace CdpMcp;

/// <summary>MEAI message/options + streaming GetStreamingResponseAsync bridge for Face Completions.</summary>
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
        // Stream = organ shape (Glass Face). Headers=TTFT until first chunk; Idle between updates; Overall hard cap.
        using var turnCts = CreateTurnCts(cancellationToken);
        try
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token);
            budgetCts.CancelAfter(HeadersTimeoutFor(built.Mode));
            void TouchIdle() => budgetCts.CancelAfter(IdleTimeout);

            var messages = BuildMeAiMessages(built);
            var options = BuildMeAiChatOptions(built, maxTokens);
            var contentSb = new StringBuilder();
            var reasoningSb = new StringBuilder();
            string? finish = null;
            int? prompt = null, completion = null, total = null;
            var gotFirst = false;

            var enumerator = client
                .GetStreamingResponseAsync(messages, options, budgetCts.Token)
                .GetAsyncEnumerator(budgetCts.Token);
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    if (!gotFirst)
                    {
                        gotFirst = true;
                        TouchIdle();
                    }
                    else
                        TouchIdle();

                    var update = enumerator.Current;
                    if (!string.IsNullOrEmpty(update.Text))
                        contentSb.Append(update.Text);
                    if (update.FinishReason is { } fr)
                        finish = fr.ToString();

                    foreach (var part in update.Contents)
                    {
                        switch (part)
                        {
                            case TextReasoningContent tr when !string.IsNullOrEmpty(tr.Text):
                                reasoningSb.Append(tr.Text);
                                break;
                            case UsageContent uc:
                                prompt ??= uc.Details?.InputTokenCount is long pin ? (int)pin : null;
                                completion ??= uc.Details?.OutputTokenCount is long cout ? (int)cout : null;
                                total ??= uc.Details?.TotalTokenCount is long tot ? (int)tot : null;
                                break;
                        }
                    }
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            string? text;
            var fromReasoning = false;
            if (contentSb.Length > 0)
                text = contentSb.ToString().Trim();
            else if (reasoningSb.Length > 0)
            {
                text = reasoningSb.ToString().Trim();
                fromReasoning = true;
            }
            else
                text = null;

            var meta = new OpenAiExtract(
                string.IsNullOrWhiteSpace(text) ? null : text,
                finish,
                fromReasoning,
                prompt,
                completion,
                total);
            return FinishText(built, resolved, string.IsNullOrWhiteSpace(text) ? null : text, meta);
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
}
