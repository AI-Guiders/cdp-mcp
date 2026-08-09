#nullable enable
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MeAiChat = Microsoft.Extensions.AI.ChatMessage;
using MeAiRole = Microsoft.Extensions.AI.ChatRole;

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

            var applied = new List<CitizenRouteHost.Applied>();
            var exec = ResolveAgentExecute();
            var tools = CitizenMeAiAgentTools.BuildWholeCatalog(exec, applied);
            var options = BuildMeAiChatOptions(built, maxTokens);
            var instructions = (built.System ?? "").TrimEnd()
                + "\n\nHabitat tools: use only listed functions. Prefer named tools when listed. "
                + "For any other CDP tool call cdp_call(tool_name, args_json). Never invent tool names.";
            AIAgent agent = client.AsAIAgent(
                instructions: instructions,
                tools: tools);

            // History messages only — system lives in AsAIAgent instructions (CIDE shape).
            var messages = BuildMeAiMessages(built)
                .Where(m => m.Role != MeAiRole.System)
                .ToList();
            if (messages.Count == 0)
                messages.Add(new MeAiChat(MeAiRole.User, "(empty)"));

            // ChatOptions MaxOutputTokens still applied via underlying client when supported.
            _ = options;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CideHandsLatch.PublishRunning();
            try
            {
                var response = agent.RunAsync(messages, cancellationToken: budgetCts.Token)
                    .GetAwaiter()
                    .GetResult();

                var text = ExtractAgentText(response);
                // Letter stays prose — SoftOrgan HND owns tool receipts (reuse HandsLatch).
                PublishAgentHands(applied, sw.Elapsed);

                var meta = new OpenAiExtract(
                    string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                    null,
                    false,
                    null,
                    null,
                    null);
                return FinishText(built, resolved, meta.Text, meta);
            }
            catch
            {
                PublishAgentHands(applied, sw.Elapsed);
                throw;
            }
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

    static void PublishAgentHands(
        IReadOnlyList<CitizenRouteHost.Applied> applied,
        TimeSpan elapsed)
    {
        if (applied.Count == 0)
        {
            CideHandsLatch.Clear();
            return;
        }

        CideHandsLatch.PublishDone(applied, elapsed);
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
        List<CitizenRouteHost.Applied> applied)
    {
        // Lived 0.5.706 silence: Meta+bare ~95 schemas → timeout/empty.
        // Lived 0.5.707 dogfood: cdp_call-only → model invents cdp_health name → Function failed.
        // Shape: thin habitat named + cdp_call escape = whole catalog reachability without thrash.
        // Receipts: SoftOrgan HND via CideHandsLatch (reuse HandsReceipt) — not letter glue.
        var list = new List<AITool>();
        foreach (var name in HabitatNamed)
            list.Add(CreateNamedThinTool(name, exec, applied));
        list.Add(CreateCdpCallDispatch(exec, applied));
        return list;
    }

    static readonly string[] HabitatNamed =
    [
        "cdp_health",
        "cdp_buffer",
        "find",
        "cdp_build",
        "cdp_shell_run",
    ];

    internal static int CountNamedCatalogTools() => HabitatNamed.Length;

    internal static int CountDispatchTools() => HabitatNamed.Length + 1;

    static AIFunction CreateNamedThinTool(
        string toolName,
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<CitizenRouteHost.Applied> applied)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("JSON object of tool args, or {}.")]
                string? args_json,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var args = ParseArgsJson(args_json);
                    var outcome = await exec(toolName, args, cancellationToken).ConfigureAwait(false);
                    var clipped = ClipOutcome(outcome, 12_000);
                    RecordApplied(applied, toolName, ok: true, pulse: $"chars={clipped.Length}");
                    return clipped;
                }
                catch (OperationCanceledException)
                {
                    RecordApplied(applied, toolName, ok: false, reason: "отмена");
                    throw;
                }
                catch (Exception ex)
                {
                    RecordApplied(applied, toolName, ok: false, reason: ex.Message);
                    return $"[{toolName}] ошибка: {ex.Message}";
                }
            },
            name: toolName,
            description: $"CDP habitat tool {toolName}. Pass args_json={{}} when none.");
    }

    static AIFunction CreateCdpCallDispatch(
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<CitizenRouteHost.Applied> applied)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("Exact CDP CallTool name (e.g. git_diff, memory_world_get_definition, find).")]
                string tool_name,
                [Description("Optional JSON object of arguments, e.g. {\"path\":\"x.cs\"}. Empty ok.")]
                string? args_json,
                CancellationToken cancellationToken) =>
            {
                tool_name = (tool_name ?? "").Trim();
                if (tool_name.Length == 0)
                {
                    RecordApplied(applied, "cdp_call", ok: false, reason: "пустой tool_name");
                    return "[cdp_call] ошибка: пустой tool_name";
                }

                var go = "cdp_call→" + tool_name;
                try
                {
                    var args = ParseArgsJson(args_json);
                    var outcome = await exec(tool_name, args, cancellationToken).ConfigureAwait(false);
                    var clipped = ClipOutcome(outcome, 12_000);
                    RecordApplied(applied, go, ok: true, pulse: $"chars={clipped.Length}");
                    return clipped;
                }
                catch (OperationCanceledException)
                {
                    RecordApplied(applied, go, ok: false, reason: "отмена");
                    throw;
                }
                catch (Exception ex)
                {
                    RecordApplied(applied, go, ok: false, reason: ex.Message);
                    return $"[cdp_call→{tool_name}] ошибка: {ex.Message}";
                }
            },
            name: "cdp_call",
            description:
                "Call any CDP tool by name that is not already listed (git_*/memory_*/cdp_pressure/…). "
                + "Pass tool_name= exact CallTool id and args_json={{}} or a JSON object. Do not invent unlisted tool names as top-level functions.");
    }

    static void RecordApplied(
        List<CitizenRouteHost.Applied> applied,
        string go,
        bool ok,
        string? pulse = null,
        string? reason = null)
    {
        applied.Add(new CitizenRouteHost.Applied(
            Raw: go,
            Verb: "agent",
            Ok: ok,
            Go: go,
            Pulse: pulse,
            Reason: reason));
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

    static string ClipOutcome(string? outcome, int maxChars)
    {
        var s = outcome ?? "";
        if (s.Length <= maxChars)
            return s;
        return s[..maxChars] + $"\n… (+{s.Length - maxChars} chars clipped)";
    }
}
