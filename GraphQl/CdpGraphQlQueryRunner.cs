#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;
using HotChocolate;
using HotChocolate.Execution;

namespace CdpMcp.GraphQl;

/// <summary>Execute a GraphQL document against the warm in-proc executor (MCP path).</summary>
internal static class CdpGraphQlQueryRunner
{
    internal sealed record Outcome(bool Ok, object? Gql, string? Error, string? Detail, string? Hint);

    internal static Outcome Run(
        SessionContext session,
        DocumentBufferStore docStore,
        CdpSettings settings,
        string query,
        IReadOnlyDictionary<string, JsonElement> args,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? dispatchToolAsync)
    {
        var executor = CdpGraphQlRuntime.EnsureExecutorAsync().GetAwaiter().GetResult();
        if (executor is null)
        {
            return new Outcome(false, null, "executor_cold", CdpGraphQlRuntime.LastWarmError,
                "Schema warm failed — see detail; fix HC registration then redeploy.");
        }

        var request = OperationRequestBuilder.New()
            .SetDocument(query)
            .SetVariableValues(ParseVariables(args))
            .SetGlobalState(CdpGraphQlCall.StateKey, new CdpGraphQlCall
            {
                Session = session,
                DocStore = docStore,
                Settings = settings,
                DispatchToolAsync = dispatchToolAsync
            })
            .Build();

        var result = executor.ExecuteAsync(request).GetAwaiter().GetResult();
        var json = result.ToJson();
        if (result is IAsyncDisposable asyncDisp)
            asyncDisp.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else if (result is IDisposable disp)
            disp.Dispose();

        using var parsed = JsonDocument.Parse(json);
        var hasErrors = parsed.RootElement.TryGetProperty("errors", out var errs)
            && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0;

        object gql = hasErrors
            ? GraphQlUnknownFieldErgonomics.EnrichMcpErrors(parsed.RootElement)
            : parsed.RootElement.Clone();

        return new Outcome(!hasErrors, gql, null, null, null);
    }

    static IReadOnlyDictionary<string, object?>? ParseVariables(IReadOnlyDictionary<string, JsonElement> args)
    {
        if (!args.TryGetValue("variables", out var varsEl))
            return null;

        if (varsEl.ValueKind == JsonValueKind.String)
        {
            var raw = varsEl.GetString();
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(raw!);
        }

        if (varsEl.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(varsEl.GetRawText());

        return null;
    }
}
