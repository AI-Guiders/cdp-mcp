#nullable enable
using System.ComponentModel;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using MeAiChat = Microsoft.Extensions.AI.ChatMessage;
using MeAiRole = Microsoft.Extensions.AI.ChatRole;
using McpTool = ModelContextProtocol.Protocol.Tool;

namespace CdpMcp;

/// <summary>
/// Face Completions MEAI agent pipe — whole CDP catalog as AIFunctions + cdp_call dispatch.
/// Parity with CascadeIdeMafIdeAgentChat (AsAIAgent), without SoftOrgan invent.
/// </summary>
internal static partial class CitizenCompletions
{
    /// <summary>Tests: force agent path even without IdeCommandModule bind.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>>?
        TestAgentExecute;

    internal static bool AgentPipeAvailable =>
        TestAgentExecute is not null || IdeCommandModule.IsBound;

    static TurnResult TurnViaMeAiAgent(
        BuiltTurn built,
        Resolved resolved,
        IChatClient client,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Agent multi-round = Overall budget (not Headers-only TTFT). Pipe, not keyboard.
        using var turnCts = CreateTurnCts(cancellationToken);
        try
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(turnCts.Token);
            budgetCts.CancelAfter(OverallTimeout);

            var toolTraces = new List<string>();
            var exec = ResolveAgentExecute();
            var tools = CitizenMeAiAgentTools.BuildWholeCatalog(exec, toolTraces);
            var options = BuildMeAiChatOptions(built, maxTokens);
            AIAgent agent = client.AsAIAgent(
                instructions: built.System,
                tools: tools);

            // History messages only — system lives in AsAIAgent instructions (CIDE shape).
            var messages = BuildMeAiMessages(built)
                .Where(m => m.Role != MeAiRole.System)
                .ToList();
            if (messages.Count == 0)
                messages.Add(new MeAiChat(MeAiRole.User, "(empty)"));

            // ChatOptions MaxOutputTokens still applied via underlying client when supported.
            _ = options;
            var response = agent.RunAsync(messages, cancellationToken: budgetCts.Token)
                .GetAwaiter()
                .GetResult();

            var text = ExtractAgentText(response);
            if (toolTraces.Count > 0 && !string.IsNullOrWhiteSpace(text))
                text = text.Trim() + "\n\n" + string.Join("\n", toolTraces.TakeLast(8));

            var meta = new OpenAiExtract(
                string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                null,
                false,
                null,
                null,
                null);
            return FinishText(built, resolved, meta.Text, meta);
        }
        catch (OperationCanceledException oce)
        {
            return MapCancel(built, resolved, oce, cancellationToken, built.Mode);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return FailNetwork(built, resolved, ex);
        }
    }

    static Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> ResolveAgentExecute()
    {
        if (TestAgentExecute is { } test)
            return test;
        return (name, args, ct) => IdeCommandModule.ExecuteAsync(name, args, ct);
    }

    static string ExtractAgentText(AgentResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
            return response.Text.Trim();

        var sb = new StringBuilder();
        foreach (var message in response.Messages)
        {
            if (message.Role != MeAiRole.Assistant)
                continue;
            foreach (var part in message.Contents)
            {
                if (part is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    sb.Append(tc.Text);
            }
        }

        return sb.ToString().Trim();
    }
}

/// <summary>Whole CDP catalog → MEAI tools (operator fork: not narrow find/buffer/build).</summary>
internal static class CitizenMeAiAgentTools
{
    internal static IList<AITool> BuildWholeCatalog(
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<string> toolTraces)
    {
        var list = new List<AITool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in MetaToolCatalog.Build().OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(tool.Name))
                continue;
            list.Add(CreateNamedCatalogTool(tool, exec, toolTraces));
        }

        foreach (var tool in IdeLanguageTools.BuildBareVerbTools().OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(tool.Name))
                continue;
            list.Add(CreateNamedCatalogTool(tool, exec, toolTraces));
        }

        // Escape hatch for domain tools (git_*/memory_*/…) not in Meta ListTools shortlist.
        list.Add(CreateCdpCallDispatch(exec, toolTraces));
        return list;
    }

    internal static int CountNamedCatalogTools()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in MetaToolCatalog.Build())
            seen.Add(t.Name);
        foreach (var t in IdeLanguageTools.BuildBareVerbTools())
            seen.Add(t.Name);
        return seen.Count;
    }

    static AIFunction CreateNamedCatalogTool(
        McpTool tool,
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<string> toolTraces)
    {
        var description = ClampDescription(tool.Description, 3600);
        return AIFunctionFactory.Create(
            async (
                [Description("Arguments object matching the MCP tool schema. Use {} when no args.")]
                JsonElement arguments,
                CancellationToken cancellationToken) =>
            {
                var header = $"[{tool.Name}]";
                toolTraces.Add($"{header} вызов…");
                try
                {
                    var args = JsonArgsToDict(arguments);
                    var outcome = await exec(tool.Name, args, cancellationToken).ConfigureAwait(false);
                    var clipped = ClipOutcome(outcome, 12_000);
                    toolTraces[^1] = $"{header} ok · chars={clipped.Length}";
                    return clipped;
                }
                catch (OperationCanceledException)
                {
                    toolTraces[^1] = $"{header} → отмена";
                    throw;
                }
                catch (Exception ex)
                {
                    toolTraces[^1] = $"{header} → ошибка: {ex.Message}";
                    return $"{header} ошибка: {ex.Message}";
                }
            },
            new AIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = description,
            });
    }

    static AIFunction CreateCdpCallDispatch(
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<string> toolTraces)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("Exact CDP CallTool name (e.g. git_diff, memory_world_get_definition, find)." )]
                string tool_name,
                [Description("Optional JSON object of arguments, e.g. {\"path\":\"x.cs\"}. Empty ok.")]
                string? args_json,
                CancellationToken cancellationToken) =>
            {
                tool_name = (tool_name ?? "").Trim();
                if (tool_name.Length == 0)
                    return "[cdp_call] ошибка: пустой tool_name";

                var header = $"[cdp_call→{tool_name}]";
                toolTraces.Add($"{header} вызов…");
                try
                {
                    var args = ParseArgsJson(args_json);
                    var outcome = await exec(tool_name, args, cancellationToken).ConfigureAwait(false);
                    var clipped = ClipOutcome(outcome, 12_000);
                    toolTraces[^1] = $"{header} ok · chars={clipped.Length}";
                    return clipped;
                }
                catch (OperationCanceledException)
                {
                    toolTraces[^1] = $"{header} → отмена";
                    throw;
                }
                catch (Exception ex)
                {
                    toolTraces[^1] = $"{header} → ошибка: {ex.Message}";
                    return $"{header} ошибка: {ex.Message}";
                }
            },
            name: "cdp_call",
            description:
                "Call any CDP tool by name (full catalog escape — domain git/memory/build/… not only Meta ListTools). Prefer named tools when listed; use this for the rest.");
    }

    static IReadOnlyDictionary<string, JsonElement>? JsonArgsToDict(JsonElement arguments)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in arguments.EnumerateObject())
            dict[p.Name] = p.Value;
        return dict;
    }

    static IReadOnlyDictionary<string, JsonElement>? ParseArgsJson(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(argsJson.Trim());
            return JsonArgsToDict(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string ClampDescription(string? text, int maxChars)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0)
            return "(See MCP schema for this tool.)";
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + $"… (+{s.Length - maxChars} chars)";
    }

    static string ClipOutcome(string? outcome, int maxChars)
    {
        var s = outcome ?? "";
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + $"\n… (+{s.Length - maxChars} chars clipped)";
    }
}
