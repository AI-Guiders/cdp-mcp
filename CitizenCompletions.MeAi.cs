#nullable enable
using System.ClientModel;
using Microsoft.Extensions.AI;
using MeAiChat = Microsoft.Extensions.AI.ChatMessage;
using MeAiRole = Microsoft.Extensions.AI.ChatRole;

namespace CdpMcp;

/// <summary>MEAI message/options + GetResponse bridge for Face Completions.</summary>
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
        using var turnCts = CreateTurnCts(cancellationToken);
        try
        {
            using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token);
            headersCts.CancelAfter(HeadersTimeoutFor(built.Mode));

            var messages = BuildMeAiMessages(built);
            var options = BuildMeAiChatOptions(built, maxTokens);
            var response = client
                .GetResponseAsync(messages, options, headersCts.Token)
                .GetAwaiter()
                .GetResult();

            var text = (response.Text ?? "").Trim();
            var meta = MetaFromMeAi(response);
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

    static OpenAiExtract MetaFromMeAi(ChatResponse response)
    {
        var text = (response.Text ?? "").Trim();
        int? prompt = response.Usage?.InputTokenCount is long pin ? (int)pin : null;
        int? completion = response.Usage?.OutputTokenCount is long cout ? (int)cout : null;
        int? total = response.Usage?.TotalTokenCount is long tot ? (int)tot : null;
        var finish = response.FinishReason?.ToString();
        return new OpenAiExtract(
            string.IsNullOrWhiteSpace(text) ? null : text,
            finish,
            FromReasoning: false,
            prompt,
            completion,
            total);
    }
}
