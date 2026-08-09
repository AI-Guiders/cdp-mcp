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
                + "\n\nHabitat tools: use only listed functions. SoftFL desk work: prefer @intent after prose (host executes open/buffer/build/test). "
                + "Named MEAI tools are for dig/verify when listed (open→cdp_open; edit→cdp_buffer). "
                + "For any other CDP tool call cdp_call(tool_name, args_json). Never invent unlisted tool names.";
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
        // Lived 0.5.712 Radio: SoftFL Mentions invents open/edit (not in thin set) → Function failed.
        // Shape: thin habitat named + invent aliases (open/edit) + cdp_call escape.
        // Receipts: SoftOrgan HND via CideHandsLatch (reuse HandsReceipt) — not letter glue.
        var list = new List<AITool>();
        foreach (var entry in HabitatNamed)
            list.Add(CreateNamedThinTool(entry, exec, applied));
        list.Add(CreateCdpCallDispatch(exec, applied));
        return list;
    }

    /// <summary>MEAI surface name → CallTool id (+ optional default arg seed).</summary>
    sealed record NamedEntry(string Name, string ToolId, string? DefaultOp = null);

    static readonly NamedEntry[] HabitatNamed =
    [
        new("cdp_health", "cdp_health"),
        new("cdp_buffer", "cdp_buffer"),
        new("find", "find"),
        new("cdp_build", "cdp_build"),
        new("cdp_test", "cdp_test"),
        new("cdp_open", "cdp_open"),
        new("cdp_shell_run", "cdp_shell_run"),
        new("open", "cdp_open"), // invent alias — SoftFL first step
        new("edit", "cdp_buffer", DefaultOp: "edit"), // invent alias → buffer edit
    ];

    internal static int CountNamedCatalogTools() => HabitatNamed.Length;

    internal static int CountDispatchTools() => HabitatNamed.Length + 1;

    static AIFunction CreateNamedThinTool(
        NamedEntry entry,
        Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<string>> exec,
        List<CitizenRouteHost.Applied> applied)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("JSON object of tool args, or {}. Prefer this over inventing extra parameters.")]
                string? args_json = null,
                [Description("Optional path= (merged into args when set).")]
                string? path = null,
                [Description("Optional op= for buffer/open tools (merged when set).")]
                string? op = null,
                [Description("Optional query= for find (merged when set).")]
                string? query = null,
                CancellationToken cancellationToken = default) =>
            {
                var toolName = entry.ToolId;
                try
                {
                    var args = MergeNamedArgs(args_json, path, op, query, entry.DefaultOp);
                    var outcome = await exec(toolName, args, cancellationToken).ConfigureAwait(false);
                    var clipped = ClipOutcome(outcome, 12_000);
                    RecordApplied(applied, entry.Name, ok: true, pulse: $"chars={clipped.Length}");
                    return clipped;
                }
                catch (OperationCanceledException)
                {
                    RecordApplied(applied, entry.Name, ok: false, reason: "отмена");
                    throw;
                }
                catch (Exception ex)
                {
                    RecordApplied(applied, entry.Name, ok: false, reason: ex.Message);
                    return $"[{entry.Name}→{toolName}] ошибка: {ex.Message}";
                }
            },
            name: entry.Name,
            description: entry.DefaultOp is { } dop
                ? $"CDP habitat {entry.Name} → {entry.ToolId} (default op={dop}). Pass args_json and/or path=/op=/query=."
                : $"CDP habitat tool {entry.Name} → {entry.ToolId}. Pass args_json={{}} and/or path=/op=/query=.");
    }

    static IReadOnlyDictionary<string, JsonElement>? MergeNamedArgs(
        string? argsJson,
        string? path,
        string? op,
        string? query,
        string? defaultOp)
    {
        Dictionary<string, JsonElement>? dict = null;
        var parsed = ParseArgsJson(argsJson);
        if (parsed is not null)
            dict = new Dictionary<string, JsonElement>(parsed, StringComparer.Ordinal);

        void Put(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            dict ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            dict[key] = JsonSerializer.SerializeToElement(value.Trim());
        }

        Put("path", path);
        Put("op", op);
        Put("query", query);
        if (defaultOp is not null && (dict is null || !dict.ContainsKey("op")))
            Put("op", defaultOp);

        return dict;
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
                string? args_json = null,
                CancellationToken cancellationToken = default) =>
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
