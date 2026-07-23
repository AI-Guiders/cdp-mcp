using System.Text;
using System.Text.Json;
using Cdp.Core;
using DotNetBuildTest.Core;
using DotnetDebugMcp;

namespace CdpMcp.Backends;

internal sealed class FindingsBackend(CdpSettings settings) : ICdpBackendModule
{
    public string Domain => CdpDomains.MemorySelfFinding;
    public bool IsEnabled => settings.Memory.Self.Finding.Enabled;
    public string HealthSummary => IsEnabled ? "up (in-proc AgentFindings.Core)" : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
        ValueTask.FromResult(AgentFindingsMcp.ToolHandlers.Handle(underlyingName, args));
}

internal sealed class FailuresBackend(CdpSettings settings) : ICdpBackendModule
{
    public string Domain => CdpDomains.MemorySelfFailure;
    public bool IsEnabled => settings.Memory.Self.Failure.Enabled;
    public string HealthSummary => IsEnabled ? "up (in-proc AgentFailures.Core)" : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
        ValueTask.FromResult(AgentFailuresMcp.ToolHandlers.Handle(underlyingName, args));
}

internal sealed class DebugBackend(CdpSettings settings) : ICdpBackendModule
{
    private static readonly Lazy<IReadOnlyList<ModelContextProtocol.Protocol.Tool>> Catalog =
        new(() => DotnetDebugMcp.ToolCatalog.Build());

    public string Domain => CdpDomains.Debug;
    public bool IsEnabled => settings.Dev.Debug.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc DotnetDebug.Core + DAP handlers)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public async ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        return underlyingName switch
        {
            "man" => ManPages.Resolve(McpArgumentHelpers.TryGetString(args, "tool", out var tool) ? tool : null),
            "debug_ping" => $"OK {DateTime.UtcNow:O} — CdpMcp/debug. Tools: {string.Join(", ", Catalog.Value.Select(t => t.Name))}.",
            "debug_set_breakpoints" => BreakpointToolHandlers.HandleSetBreakpoints(args),
            "debug_list_breakpoints" => BreakpointToolHandlers.HandleListBreakpoints(args),
            "debug_clear_breakpoints" => BreakpointToolHandlers.HandleClearBreakpoints(args),
            "debug_launch" => await DebugLaunchToolHandlers.HandleDebugLaunch(args),
            "debug_attach" => await DebugLaunchToolHandlers.HandleDebugAttach(args),
            "debug_continue" => await DebugControlToolHandlers.HandleDebugContinue(args),
            "debug_step_over" => await DebugControlToolHandlers.HandleDebugStepOver(args),
            "debug_step_into" => await DebugControlToolHandlers.HandleDebugStepInto(args),
            "debug_step_out" => await DebugControlToolHandlers.HandleDebugStepOut(args),
            "debug_stop" => await DebugControlToolHandlers.HandleDebugStop(args),
            "debug_stop_context" => await DebugControlToolHandlers.HandleDebugStopContext(args),
            "debug_stack_trace" => await DebugControlToolHandlers.HandleDebugStackTrace(args),
            "debug_variables" => await DebugControlToolHandlers.HandleDebugVariables(args),
            "debug_variable_children" => await DebugControlToolHandlers.HandleDebugVariableChildren(args),
            _ => throw new ArgumentException($"Unknown debug tool: {underlyingName}.")
        };
    }
}

internal sealed class BuildTestBackend(CdpSettings settings) : ICdpBackendModule
{
    private readonly BuildTestJobService _jobs = new();

    public string Domain => CdpDomains.Build;
    public bool IsEnabled => settings.Dev.Build.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc DotNetBuildTest.Core)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public async ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
        underlyingName switch
        {
            "build_structured" => await _jobs.BuildStructuredAsync(args, CancellationToken.None).ConfigureAwait(false),
            "run_tests" => await _jobs.RunTestsAsync(args, CancellationToken.None).ConfigureAwait(false),
            "publish_structured" => await _jobs.PublishStructuredAsync(args, CancellationToken.None).ConfigureAwait(false),
            "get_job_status" => _jobs.GetJobStatus(args),
            "get_job_log" => _jobs.GetJobLog(args),
            "cancel_job" => _jobs.CancelJob(args),
            _ => throw new ArgumentException($"Unknown build tool: {underlyingName}.")
        };
}

internal sealed class RoslynBackend(CdpSettings settings) : ICdpBackendModule
{
    public string Domain => CdpDomains.Roslyn;
    public bool IsEnabled => settings.Dev.Roslyn.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc AIGuiders.RoslynMcp.Core + ToolHandlers)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public async ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (NeedsMsBuild(underlyingName, args))
            RoslynMcp.ServiceLayer.MsBuildLocatorOnce.EnsureRegistered();
        return await RoslynMcp.Mcp.ToolHandlers.HandleAsync(underlyingName, args, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Syntax-only / parse tools must not touch MSBuildLocator — RegisterDefaults can wedge under parallel load.
    /// </summary>
    static bool NeedsMsBuild(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        if (underlyingName is "roslyn_ping" or "roslyn_get_document_symbols")
            return false;

        if (underlyingName == "roslyn_get_diagnostics")
        {
            if (args.TryGetValue("scope", out var scopeEl)
                && scopeEl.GetString() is { } scope
                && (scope.Equals("syntax", StringComparison.OrdinalIgnoreCase)
                    || scope.Equals("parse", StringComparison.OrdinalIgnoreCase)
                    || scope.Equals("file", StringComparison.OrdinalIgnoreCase)))
                return false;

            // Default in Core: file_path set → syntax — match that without registering MSBuild.
            if (args.TryGetValue("file_path", out var fp)
                && fp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(fp.GetString())
                && (!args.TryGetValue("scope", out var s2)
                    || s2.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    || string.IsNullOrWhiteSpace(s2.GetString())))
                return false;
        }

        return true;
    }
}

internal sealed class GitBackend(CdpSettings settings) : ICdpBackendModule
{
    public string Domain => CdpDomains.Git;
    public bool IsEnabled => settings.Dev.Git.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc AIGuiders.GitMcp.Core + git CLI)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args) =>
        ValueTask.FromResult(GitMcp.ToolHandlers.Handle(underlyingName, args));
}

internal sealed class CodebaseIndexBackend(CdpSettings settings) : ICdpBackendModule
{
    public string Domain => CdpDomains.CodebaseIndex;
    public bool IsEnabled => settings.Dev.CodebaseIndex.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc AIGuiders.HybridCodebaseIndex.Core)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    /// <summary>
    /// Agent-IDE comfort: search with missing/empty index → harness reindexes once and retries.
    /// Standalone HCI MCP still returns missKind without auto (empty ≠ auto-reindex there).
    /// </summary>
    public ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        var raw = HybridCodebaseIndex.Mcp.ToolHandlers.Handle(underlyingName, args);
        if (!IsSearchLike(underlyingName)
            || !TryReadMissKind(raw, out var miss)
            || miss is not ("index_missing" or "index_empty")
            || !args.TryGetValue("workspace_path", out var ws)
            || ws.ValueKind != JsonValueKind.String)
            return ValueTask.FromResult(raw);

        var reindexArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["workspace_path"] = ws
        };
        if (args.TryGetValue("solution_path", out var sln) && sln.ValueKind == JsonValueKind.String)
            reindexArgs["solution_path"] = sln;

        var reindexRaw = HybridCodebaseIndex.Mcp.ToolHandlers.Handle("codebase_index_reindex", reindexArgs);
        raw = HybridCodebaseIndex.Mcp.ToolHandlers.Handle(underlyingName, args);
        return ValueTask.FromResult(AnnotateHarnessReindex(raw, reindexRaw));
    }

    private static bool IsSearchLike(string underlyingName) =>
        underlyingName is "codebase_index_search";

    private static bool TryReadMissKind(string json, out string? missKind)
    {
        missKind = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("missKind", out var mk)
                || mk.ValueKind != JsonValueKind.String)
                return false;
            missKind = mk.GetString();
            return !string.IsNullOrEmpty(missKind);
        }
        catch
        {
            return false;
        }
    }

    private static string AnnotateHarnessReindex(string searchJson, string reindexJson)
    {
        try
        {
            using var search = JsonDocument.Parse(searchJson);
            using var reindex = JsonDocument.Parse(reindexJson);
            var root = search.RootElement.Clone();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var p in root.EnumerateObject())
                    p.WriteTo(writer);
                writer.WriteBoolean("harnessAutoReindexed", true);
                writer.WritePropertyName("harnessReindex");
                reindex.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return searchJson;
        }
    }
}

internal sealed class AnuiBackend(CdpSettings settings) : ICdpBackendModule
{
    private readonly Lazy<(Anui.Agent.AgentChannel Channel, Anui.Adapters.EvidenceAdapterRegistry Registry)> _session =
        new(() =>
        {
            var (channel, registry, _) = Anui.Agent.EvidenceSessionFactory.Create();
            return (channel, registry);
        });

    public string Domain => CdpDomains.Anui;
    public bool IsEnabled => settings.Dev.Anui.Enabled;
    public string HealthSummary => IsEnabled
        ? "up (in-proc Anui.Agent + Avalonia headless)"
        : "disabled";
    public IReadOnlyList<ToolAffordance> Affordances =>
        Wave1AffordanceSeed.Build().Where(a => a.Domain == Domain).ToArray();

    public async ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, JsonElement> args)
    {
        var (channel, registry) = _session.Value;
        return await Anui.Agent.Mcp.ToolHandlers.HandleAsync(
                underlyingName, channel, registry, args, CancellationToken.None)
            .ConfigureAwait(false);
    }
}

